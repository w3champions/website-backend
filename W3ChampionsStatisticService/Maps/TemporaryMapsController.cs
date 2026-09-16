using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using W3C.Domain.Maps;
using W3C.Domain.MatchmakingService;
using W3C.Domain.MatchmakingService.Contracts;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Sessions;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using static W3ChampionsStatisticService.Maps.TemporaryMapUploadRules;

namespace W3ChampionsStatisticService.Maps;

/// <summary>
/// Player-facing routes for self-provided (temporary) custom maps — design spec §6.1. Unlike <see cref="MapsController"/>
/// these are NOT admin-gated: any logged-in player may pre-check and upload, bounded by the in-memory quotas and the
/// in-flight gate. Both actions are authenticated by <see cref="BearerRequiresPlayerAuthAttribute"/>, an authorization
/// filter, so the 401 is written before any request body byte is read; both fail closed with 401 should the filter have
/// left no battleTag behind. Appendix A pins every body these actions answer, so each failure is mapped here and nothing
/// reaches the global exception filters (whose ErrorResult envelope A.3/A.4 do not allow).
/// <para>NEVER log mapProof or proofHash (design spec §10.3); sha1, map ids, fileKeys and battleTags are fine.</para>
/// </summary>
[ApiController]
[Route("api/maps/temporary")]
[Trace]
public class TemporaryMapsController(
    TemporaryMapUploadService uploadService,
    MatchmakingServiceClient matchmakingServiceClient,
    MintRateLimiter rateLimiter,
    TemporaryMapUploadGate uploadGate,
    ILogger<TemporaryMapsController> logger) : ControllerBase
{
    private readonly TemporaryMapUploadService _uploadService = uploadService;
    private readonly MatchmakingServiceClient _matchmakingServiceClient = matchmakingServiceClient;
    private readonly MintRateLimiter _rateLimiter = rateLimiter;
    private readonly TemporaryMapUploadGate _uploadGate = uploadGate;
    private readonly ILogger<TemporaryMapsController> _logger = logger;

    /// <summary>
    /// Pre-check keyed by proofHash — a hash of a secret only a holder of the file can compute — so it cannot be used to
    /// test whether a publicly known map is on the server. It returns the state and NOTHING else: no id, path, name, sha1
    /// or proof (Appendix A.4); a bare 429 over the per-battleTag quota; a bare 502 for anything matchmaking cannot answer
    /// (S3), including a fileState this service does not know; and nothing at all for a client that is gone.
    /// </summary>
    [HttpGet("status")]
    [BearerRequiresPlayerAuth]
    public async Task<IActionResult> GetStatus([FromQuery][NoTrace] string proofHash, CancellationToken cancellationToken)
    {
        var battleTag = BattleTag();
        if (battleTag == null)
        {
            return FailClosed();
        }

        if (!_rateLimiter.TryAcquire($"tm-precheck:{battleTag}", TemporaryMapLimits.PrecheckPerBattleTagPerMinute, DateTime.UtcNow,
                TemporaryMapLimits.PrecheckQuotaWindow, out _))
        {
            // A.4: "429. Nothing else is returned."
            return StatusCode(StatusCodes.Status429TooManyRequests);
        }

        // A malformed key can never match a record, and answering "unknown" locally keeps it from becoming a free
        // upstream probe (the client would also refuse it as an argument).
        if (!MapProof.IsLowercaseHex(proofHash, TemporaryMapKeys.ProofHashHexLength))
        {
            return Unknown();
        }

        TemporaryMapStateResponse state;
        try
        {
            state = await _matchmakingServiceClient.GetTemporaryMapStateByProofHash(proofHash, cancellationToken);
        }
        catch (Exception) when (RequestAborted)
        {
            // The client went away while matchmaking was asked: whatever escaped — the cancelled read, a contract
            // violation the client raised after the body was read — nobody is listening and nothing is worth a Warning.
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            // Everything else — a transport failure, an HttpClient timeout, a route-level 404, a contract violation — is
            // an upstream failure. Logged by type and status only (S-L3): matchmaking echoes request text in its error
            // bodies, and this request's text is the proofHash.
            _logger.LogWarning("Temporary map pre-check could not be answered by matchmaking: {ExceptionType} (status {StatusCode})",
                ex.GetType().Name, StatusOf(ex));
            return StatusCode(StatusCodes.Status502BadGateway);
        }

        return AnswerState(state);
    }

    /// <summary>
    /// The A.4 answer for what matchmaking returned: no record is <c>unknown</c>; a record is relayed only as
    /// <c>ready</c> or <c>expired</c>, from exactly the two fileStates this route knows. Anything else — a value it does
    /// not know, a missing one — is a bare 502: relaying it would send the launcher down a path it has no code for. This
    /// validation holds on its own; the client's own contract check is not relied on.
    /// </summary>
    internal IActionResult AnswerState(TemporaryMapStateResponse state)
    {
        if (state == null)
        {
            return Unknown();
        }

        switch (state.FileState)
        {
            case TemporaryMapFileStates.Present:
                return Ok(new { state = "ready" });
            case TemporaryMapFileStates.Deleted:
                return Ok(new { state = "expired" });
            default:
                _logger.LogWarning("Temporary map pre-check: matchmaking answered fileState {FileState}, which this route cannot relay",
                    LoggableFileState(state.FileState));
                return StatusCode(StatusCodes.Status502BadGateway);
        }
    }

    /// <summary>
    /// Streams a multipart upload (metadata then mapFile) straight off the wire. 201 for a new record, 200 for a dedupe
    /// hit or a restore; the response body never carries a secret. An in-flight slot (one per battleTag, eight per
    /// process) is taken before the first body byte and released once the service has returned, compensation included.
    /// </summary>
    [HttpPost]
    [BearerRequiresPlayerAuth]
    [DisableFormValueModelBinding]
    [TemporaryMapUploadBodyLimit]
    public async Task<IActionResult> Upload(CancellationToken cancellationToken)
    {
        var battleTag = BattleTag();
        if (battleTag == null)
        {
            return FailClosed();
        }

        if (!_uploadGate.TryAcquire(battleTag, out var slot, out var refusedBy))
        {
            // Which bound refused, and how full the gate is — never who holds the other slots.
            _logger.LogInformation("Temporary map upload from {BattleTag} refused by the {Bound} bound ({InFlight} of {MaxConcurrentUploads} slots taken)",
                battleTag, refusedBy, _uploadGate.InFlight, TemporaryMapLimits.MaxConcurrentUploads);
            // A 4xx the client caused, so the label the service would give a 429 (TemporaryMapMetrics.ResultOf).
            TemporaryMapMetrics.Uploads.WithLabels(TemporaryMapMetrics.Results.Rejected).Inc();
            return StatusCode(StatusCodes.Status429TooManyRequests,
                TemporaryMapFailureBodies.QuotaExceeded(TemporaryMapLimits.ConcurrentUploadRetryAfterSeconds));
        }

        using (slot)
        {
            try
            {
                var outcome = await _uploadService.HandleUploadAsync(Request.Body, Request.ContentType, battleTag, cancellationToken);
                return StatusCode(outcome.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK, outcome.Response);
            }
            catch (Exception) when (RequestAborted)
            {
                // The client went away: whatever escaped — the body read, a pre-store call, an A.3 rejection, a spool
                // fault — nobody is listening (Task 5a §9, item 1). Checked before any status is chosen, because Kestrel
                // cancels RequestAborted before the read fails. Whatever the service counted, it counted by outcome.
                _logger.LogInformation("Temporary map upload from {BattleTag} was abandoned by the client", battleTag);
                return new EmptyResult();
            }
            catch (TemporaryMapUploadException ex)
            {
                // Appendix A.3 pins these bodies, so they are returned directly rather than through the global
                // HttpRequestExceptionFilter's ErrorResult envelope.
                return StatusCode(ex.StatusCode, ex.Body);
            }
            catch (TemporaryMapSpoolException)
            {
                // A local disk fault, already logged at Error by the reader: the client did nothing wrong (not 400) and no
                // upstream was involved (not 502). A.3 defines no body for it. Not logged again here, and never left to the
                // framework, which would log it a second time.
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
            catch (BadHttpRequestException ex)
            {
                // Kestrel's own request rejections: its body-size limit (413) before the reader's cap, anything else a
                // malformed request (I1/D1 (c)).
                return ex.StatusCode == StatusCodes.Status413PayloadTooLarge
                    ? StatusCode(StatusCodes.Status413PayloadTooLarge, TemporaryMapFailureBodies.Coded(TemporaryMapErrorCodes.FileTooLarge))
                    : StatusCode(StatusCodes.Status400BadRequest, TemporaryMapFailureBodies.Coded(TemporaryMapErrorCodes.Metadata));
            }
            catch (IOException)
            {
                // The body ended early or the connection was reset without an abort: the A.3 code for a malformed body.
                return StatusCode(StatusCodes.Status400BadRequest, TemporaryMapFailureBodies.Coded(TemporaryMapErrorCodes.Metadata));
            }
            catch (Exception ex)
            {
                // Nothing the contract lets escape: a cancellation the request did not cause, or a fault in the body
                // stream itself. Logged here, once, and answered as the server fault it is (counted server_error by the
                // service, TemporaryMapMetrics.ResultOf).
                _logger.LogError(ex, "Temporary map upload from {BattleTag} failed unexpectedly", battleTag);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }
    }

    /// <summary>The battleTag the auth filter published, or null when there is none to act for.</summary>
    private string BattleTag()
        => HttpContext.Items.TryGetValue(BearerRequiresPlayerAuthFilter.BattleTagItemKey, out var value)
           && value is string battleTag
           && !string.IsNullOrWhiteSpace(battleTag)
            ? battleTag
            : null;

    private bool RequestAborted => HttpContext.RequestAborted.IsCancellationRequested;

    /// <summary>Defence in depth: the authorization filter answers 401 itself; this is for a route it did not run on.</summary>
    private IActionResult FailClosed()
    {
        _logger.LogWarning("Temporary map route {Action} reached without a player battleTag; answering 401", ControllerContext.ActionDescriptor?.DisplayName);
        return Unauthorized();
    }

    private IActionResult Unknown() => NotFound(new { state = "unknown" });
}
