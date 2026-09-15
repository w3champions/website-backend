using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using W3C.Contracts.Matchmaking;
using W3C.Domain.Maps;
using W3C.Domain.MatchmakingService;
using W3C.Domain.MatchmakingService.Contracts;
using W3C.Domain.UpdateService;
using W3C.Domain.UpdateService.Contracts;
using W3ChampionsStatisticService.Sessions;
using static W3ChampionsStatisticService.Maps.TemporaryMapUploadRules;

namespace W3ChampionsStatisticService.Maps;

/// <summary>
/// The §6.3 upload orchestration. Three paths come out of the sha1 dedupe probe: a present record (nothing stored, no
/// quota), a deleted record (verify the proof WITHOUT mutating, store the bytes at the record's own path, then flip
/// fileState to present), and no record (spend quota, store the bytes, create the record).
/// <para>
/// Before the bytes are stored every call takes the request's token, and a failure DURING the update-service upload is
/// never compensated: whether the bytes landed is unknown, and the fileKey may back another record. Once they are
/// stored everything is uncancellable; a definite failure compensates with an update-service delete, and an ambiguous
/// matchmaking write re-probes by sha1 once to decide.
/// </para>
/// <para>NEVER log mapProof, proofHash or MapProofHash (design spec §10.3). sha1, map ids and fileKeys are fine.</para>
/// </summary>
public class TemporaryMapUploadService(
    MatchmakingServiceClient matchmakingServiceClient,
    UpdateServiceClient updateServiceClient,
    MintRateLimiter rateLimiter,
    ILogger<TemporaryMapUploadService> logger)
{
    private const string UpdateServiceRoot = "W3Champions/";

    /// <summary>§6.3 step 9: after a failed compensation delete, retry after 1, 2 and 4 s.</summary>
    private static readonly TimeSpan[] CompensationDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)];

    private readonly MatchmakingServiceClient _matchmakingServiceClient = matchmakingServiceClient;
    private readonly UpdateServiceClient _updateServiceClient = updateServiceClient;
    private readonly MintRateLimiter _rateLimiter = rateLimiter;
    private readonly ILogger<TemporaryMapUploadService> _logger = logger;

    /// <summary>Test seam: the spool directory; null means <see cref="TemporaryMapLimits.TempUploadDir"/>.</summary>
    internal string SpoolDirectory { get; init; }

    /// <summary>Test seam: how compensation waits before each retry. Never cancellable.</summary>
    internal Func<TimeSpan, Task> WaitAsync { get; init; } = delay => Task.Delay(delay, CancellationToken.None);

    /// <summary>
    /// Spools the body, then orchestrates. Escapes: <see cref="TemporaryMapUploadException"/> (its status and body), the
    /// reader's <see cref="TemporaryMapSpoolException"/>, an <see cref="IOException"/> from reading the body, and
    /// <see cref="OperationCanceledException"/> once <paramref name="cancellationToken"/> is cancelled. Every outcome but
    /// a client abort is counted once in <see cref="TemporaryMapMetrics.Uploads"/>.
    /// </summary>
    public async Task<TemporaryMapUploadOutcome> HandleUploadAsync(
        Stream body, string contentType, string battleTag, CancellationToken cancellationToken)
    {
        try
        {
            using var upload = await TemporaryMapUploadReader.ReadAsync(
                body, contentType, SpoolDirectory ?? TemporaryMapLimits.TempUploadDir, TemporaryMapLimits.MaxFileBytes, cancellationToken);
            var (outcome, result) = await OrchestrateAsync(upload, battleTag, cancellationToken);
            TemporaryMapMetrics.Uploads.WithLabels(result).Inc();
            return outcome;
        }
        catch (Exception ex) when (!(cancellationToken.IsCancellationRequested && ex is OperationCanceledException or IOException))
        {
            TemporaryMapMetrics.Uploads.WithLabels(ex is TemporaryMapUploadException { StatusCode: < 500 } or IOException
                ? TemporaryMapMetrics.Results.Rejected
                : TemporaryMapMetrics.Results.UpstreamError).Inc();
            throw;
        }
    }

    private async Task<(TemporaryMapUploadOutcome, string)> OrchestrateAsync(
        TemporaryMapUpload upload, string battleTag, CancellationToken cancellationToken)
    {
        // 1. The client's sha1 is a hint only; the authoritative one came off the wire.
        if (!string.Equals(upload.Metadata.Sha1?.ToLowerInvariant(), upload.Sha1, StringComparison.Ordinal))
        {
            throw new TemporaryMapUploadException(StatusCodes.Status400BadRequest, "SHA1_MISMATCH");
        }

        _logger.LogInformation("Temporary map upload from {BattleTag}: sha1 {Sha1}, {SizeBytes} bytes, launcher {LauncherVersion}",
            battleTag, upload.Sha1, upload.SizeBytes, upload.Metadata.Capture?.LauncherVersion);

        // 2. Dedupe by sha1.
        var existing = await BeforeStoreAsync("sha1 dedupe probe",
            ct => _matchmakingServiceClient.GetTemporaryMapBySha1(upload.Sha1, ct), cancellationToken);
        var proofHash = MapProof.Hash(upload.MapProof);
        return existing?.FileState switch
        {
            _ when existing == null => await CreateAsync(upload, proofHash, battleTag, cancellationToken),
            TemporaryMapFileStates.Present => Deduped(existing, upload.Sha1),
            TemporaryMapFileStates.Deleted => await RestoreAsync(existing, upload, proofHash, battleTag, cancellationToken),
            _ => throw UnknownFileState(existing.Id, existing.FileState),
        };
    }

    private async Task<(TemporaryMapUploadOutcome, string)> CreateAsync(
        TemporaryMapUpload upload, string proofHash, string battleTag, CancellationToken cancellationToken)
    {
        // 3. Quota: only a genuinely new record costs one. MintRateLimiter has no release, so the token is kept even
        // when the update-service 409 re-probe below turns this upload into a dedupe — a race-only overcount (S5).
        if (!_rateLimiter.TryAcquire($"tm-upload:{battleTag}", TemporaryMapLimits.UploadsPerHourPerBattleTag, DateTime.UtcNow,
                TemporaryMapLimits.UploadQuotaWindow, out var retryAfter))
        {
            throw new TemporaryMapUploadException(StatusCodes.Status429TooManyRequests, "QUOTA_EXCEEDED",
                new { code = "QUOTA_EXCEEDED", retryAfterSeconds = RetryAfterSeconds(retryAfter) });
        }

        // 4. Server-generated fileKey: the uploader's name only ever contributes a sanitised base.
        var fileKey = TemporaryMapNaming.BuildFileKey(upload.Metadata.OriginalFileName, upload.Sha1, upload.Extension);
        var stored = await StoreBytesAsync(upload, fileKey, mapId: 0, battleTag, cancellationToken);
        if (stored == null)
        {
            // update-service already holds this fileKey. If matchmaking knows our sha1 by now, that record wins.
            var known = await BeforeStoreAsync("sha1 re-probe after an update-service conflict",
                ct => _matchmakingServiceClient.GetTemporaryMapBySha1(upload.Sha1, ct), cancellationToken);
            if (known != null)
            {
                return known.FileState == TemporaryMapFileStates.Present
                    ? Deduped(known, upload.Sha1)
                    : throw UnknownFileState(known.Id, known.FileState);
            }

            stored = await ReplaceStrayFileAsync(upload, fileKey, mapId: 0, battleTag, cancellationToken);
        }

        return await AfterStoreAsync(fileKey, async () =>
        {
            await VerifyDigestsAsync(stored, upload, proofHash, fileKey);

            // 6. The capture has to describe a lobby this map can actually have (new maps only).
            if (!IsPossibleLayout(upload.Metadata.Capture, stored.MetaData))
            {
                _logger.LogInformation("Upload sha1 {Sha1} captured a lobby this map cannot have; compensating {FileKey}", upload.Sha1, fileKey);
                await CompensateAsync(fileKey);
                throw new TemporaryMapUploadException(StatusCodes.Status400BadRequest, "INVALID_LAYOUT");
            }

            return await CreateRecordAsync(upload, stored.MetaData, fileKey, battleTag);
        });
    }

    private async Task<(TemporaryMapUploadOutcome, string)> CreateRecordAsync(
        TemporaryMapUpload upload, GameMap parsed, string fileKey, string battleTag)
    {
        // 7. Create the record.
        var capture = upload.Metadata.Capture;
        CreateTemporaryMapResult created;
        try
        {
            created = await _matchmakingServiceClient.CreateTemporaryMap(new CreateTemporaryMapRequest
            {
                Sha1 = upload.Sha1,
                Uploader = battleTag,
                OriginalFileName = upload.Metadata.OriginalFileName,
                MapProof = upload.MapProof,
                MaxTeams = capture.MaxTeams,
                SlotCount = capture.SlotCount,
                LobbyMode = capture.LobbyMode,
                MappedForces = capture.MappedForces,
                GameMap = ToGameMap(parsed, fileKey),
            }, CancellationToken.None);
        }
        catch (Exception ex) when (IsDefiniteFailure(ex, conflictIsAmbiguous: true))
        {
            throw await CompensateAsync(fileKey, ex, "matchmaking refused to create temporary map sha1 {Sha1}", upload.Sha1);
        }
        catch (Exception ex) when (IsAmbiguousFailure(ex, conflictIsAmbiguous: true))
        {
            _logger.LogWarning(ex, "Creating temporary map {FileKey} went unanswered; asking matchmaking by sha1 {Sha1}", fileKey, upload.Sha1);
            var record = await ProbeAfterStoreAsync(upload.Sha1, fileKey)
                         ?? throw await CompensateAsync(fileKey, null, "No temporary map has sha1 {Sha1} after the unanswered create", upload.Sha1);
            return await KeepTheWinnerAsync(record, upload.Sha1, fileKey);
        }

        if (!created.Created)
        {
            return await KeepTheWinnerAsync(created.Map, upload.Sha1, fileKey);
        }

        _logger.LogInformation("Created temporary map {MapId} at {FileKey} for {BattleTag}", created.Map.Id, fileKey, battleTag);
        return Outcome(TemporaryMapMetrics.Results.Created, created.Map.Id, fileKey, created.Map.Name, upload.Sha1);
    }

    /// <summary>Another upload of this sha1 owns the record. Our file is stray only if that record lives at a different path.</summary>
    private async Task<(TemporaryMapUploadOutcome, string)> KeepTheWinnerAsync(MapContract winner, string sha1, string fileKey)
    {
        if (!string.Equals(winner.Path, fileKey, StringComparison.Ordinal))
        {
            await CompensateAsync(fileKey);
        }

        return Deduped(winner, sha1);
    }

    private async Task<(TemporaryMapUploadOutcome, string)> RestoreAsync(
        MapContract existing, TemporaryMapUpload upload, string proofHash, string battleTag, CancellationToken cancellationToken)
    {
        // 4a. VERIFY BEFORE MUTATE: this route writes nothing, so a wrong proof costs no state.
        var verified = await BeforeStoreAsync("proof verification",
                           ct => _matchmakingServiceClient.VerifyTemporaryMapProof(proofHash, ct), cancellationToken)
                       ?? throw new TemporaryMapUploadException(StatusCodes.Status400BadRequest, "PROOF_MISMATCH");
        if (verified.MapId != existing.Id)
        {
            // sha1 is the dedupe key and proofHash the credential key; both derive from the same bytes (§5.1).
            _logger.LogError("TEMP_MAP_KEY_MISMATCH: sha1 {Sha1} resolves to map {Sha1MapId} but its proof to map {ProofMapId}",
                upload.Sha1, existing.Id, verified.MapId);
            throw KeyMismatch();
        }

        if (verified.FileState == TemporaryMapFileStates.Present)
        {
            // A concurrent restore already put the bytes back: a dedupe hit, nothing written (S4).
            return Deduped(existing, upload.Sha1);
        }

        if (verified.FileState != TemporaryMapFileStates.Deleted)
        {
            throw UnknownFileState(verified.MapId, verified.FileState);
        }

        // 4b. The record's stored path is authoritative; the uploader's file name plays no part.
        var fileKey = verified.Path;
        if (!TemporaryMapKeys.IsFilePath(fileKey))
        {
            _logger.LogError("TEMP_MAP_KEY_MISMATCH: temporary map {MapId} stores {FileKey}, which is not a temporary map file", existing.Id, fileKey);
            throw KeyMismatch();
        }

        var stored = await StoreBytesAsync(upload, fileKey, existing.Id, battleTag, cancellationToken)
                     ?? await ReplaceStrayFileAsync(upload, fileKey, existing.Id, battleTag, cancellationToken);

        return await AfterStoreAsync(fileKey, async () =>
        {
            // The capture is not validated on a restore: the record's layout is authoritative (B4).
            await VerifyDigestsAsync(stored, upload, proofHash, fileKey);
            await MarkRestoredAsync(existing.Id, upload, fileKey, battleTag);
            _logger.LogInformation("Restored temporary map {MapId} at {FileKey} for {BattleTag}", existing.Id, fileKey, battleTag);
            return Outcome(TemporaryMapMetrics.Results.Restored, existing.Id, fileKey, existing.Name, upload.Sha1);
        });
    }

    private async Task MarkRestoredAsync(int mapId, TemporaryMapUpload upload, string fileKey, string battleTag)
    {
        // 4c. Only now, with verified bytes stored, does the record change.
        try
        {
            await _matchmakingServiceClient.MarkTemporaryMapFileRestored(mapId,
                new TemporaryMapFileRestoredRequest { Sha1 = upload.Sha1, Uploader = battleTag, MapProof = upload.MapProof },
                CancellationToken.None);
        }
        catch (Exception ex) when (IsDefiniteFailure(ex, conflictIsAmbiguous: false))
        {
            throw await CompensateAsync(fileKey, ex, "matchmaking refused file-restored for temporary map {MapId}", mapId);
        }
        catch (Exception ex) when (IsAmbiguousFailure(ex, conflictIsAmbiguous: false))
        {
            _logger.LogWarning(ex, "file-restored for temporary map {MapId} went unanswered; asking matchmaking by sha1 {Sha1}", mapId, upload.Sha1);
            var record = await ProbeAfterStoreAsync(upload.Sha1, fileKey);
            if (record?.FileState == TemporaryMapFileStates.Present)
            {
                return;
            }

            if (record != null && record.FileState != TemporaryMapFileStates.Deleted)
            {
                throw UnknownFileState(record.Id, record.FileState);
            }

            throw await CompensateAsync(fileKey, null, "Temporary map sha1 {Sha1} is still not present after the unanswered file-restored", upload.Sha1);
        }
    }

    /// <summary>
    /// update-service answered 409 for <paramref name="fileKey"/>. Deleting that file is only safe when no OTHER record claims
    /// the path (H1: the 8-hex fileKey suffix can be ground to collide with a live map). Returns the one retried upload.
    /// </summary>
    private async Task<MapFileData> ReplaceStrayFileAsync(
        TemporaryMapUpload upload, string fileKey, int mapId, string battleTag, CancellationToken cancellationToken)
    {
        var claimant = await BeforeStoreAsync("path probe after an update-service conflict",
            ct => _matchmakingServiceClient.GetTemporaryMapByPath(fileKey, ct), cancellationToken);
        if (claimant != null && (mapId == 0 || claimant.Id != mapId))
        {
            throw Upstream("Refusing to replace {FileKey}: temporary map {ClaimantMapId} (sha1 {ClaimantSha1}) claims it, " +
                           "not this upload of sha1 {Sha1} for map {MapId} (0 is a new map)",
                fileKey, claimant.Id, claimant.GameMap?.Sha1, upload.Sha1, mapId);
        }

        await BeforeStoreAsync("stray file delete", async ct =>
        {
            await _updateServiceClient.DeleteMapFileByPathAsync(fileKey, ct);
            return true;
        }, cancellationToken);

        return await StoreBytesAsync(upload, fileKey, mapId, battleTag, cancellationToken)
               ?? throw Upstream("update-service still reports a conflict at {FileKey} after deleting the stray file", fileKey);
    }

    /// <summary>Returns null when update-service reported a duplicate target path (409).</summary>
    private async Task<MapFileData> StoreBytesAsync(
        TemporaryMapUpload upload, string fileKey, int mapId, string battleTag, CancellationToken cancellationToken)
    {
        try
        {
            // update-service disposes the stream with its request, so every attempt opens its own.
            await using var bytes = upload.OpenRead();
            return await _updateServiceClient.UploadTemporaryMapAsync(bytes, fileKey[UpdateServiceRoot.Length..], mapId, battleTag, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            return null;
        }
        catch (Exception ex) when (!IsCancellation(ex, cancellationToken))
        {
            _logger.LogWarning(ex, "update-service upload of {FileKey} failed; not compensated, whether the bytes landed is unknown", fileKey);
            throw Upstream();
        }
    }

    /// <summary>A call before the bytes are stored: a client abort propagates, any other failure is a 502 with nothing to undo.</summary>
    private async Task<T> BeforeStoreAsync<T>(string step, Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        try
        {
            return await call(cancellationToken);
        }
        catch (Exception ex) when (!IsCancellation(ex, cancellationToken))
        {
            _logger.LogWarning(ex, "Temporary map upload failed at the {Step}, before any bytes were stored", step);
            throw Upstream();
        }
    }

    /// <summary>After the bytes are stored, an exception no step decided on compensates (I1(b)).</summary>
    private async Task<(TemporaryMapUploadOutcome, string)> AfterStoreAsync(string fileKey, Func<Task<(TemporaryMapUploadOutcome, string)>> steps)
    {
        try
        {
            return await steps();
        }
        catch (Exception ex) when (ex is not TemporaryMapUploadException)
        {
            _logger.LogError(ex, "Unexpected failure after storing temporary map file {FileKey}; compensating", fileKey);
            await CompensateAsync(fileKey);
            throw Upstream();
        }
    }

    /// <summary>The single sha1 re-probe after an unanswered write. If it fails too, nothing is compensated.</summary>
    private async Task<MapContract> ProbeAfterStoreAsync(string sha1, string fileKey)
    {
        try
        {
            return await _matchmakingServiceClient.GetTemporaryMapBySha1(sha1, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not re-probe sha1 {Sha1}; leaving {FileKey} for the reconciliation sweep", sha1, fileKey);
            throw Upstream();
        }
    }

    private async Task VerifyDigestsAsync(MapFileData stored, TemporaryMapUpload upload, string proofHash, string fileKey)
    {
        // 5. Both services derived these from the same bytes; a mismatch means one of them read different bytes.
        var parsedSha1 = stored.MetaData?.Sha1?.ToLowerInvariant();
        if (string.Equals(parsedSha1, upload.Sha1, StringComparison.Ordinal)
            && string.Equals(stored.MapProofHash, proofHash, StringComparison.Ordinal))
        {
            return;
        }

        _logger.LogWarning("update-service derived other digests for sha1 {Sha1} (parsed sha1 {ParsedSha1}); compensating {FileKey}",
            upload.Sha1, parsedSha1, fileKey);
        await CompensateAsync(fileKey);
        throw new TemporaryMapUploadException(StatusCodes.Status502BadGateway, "PARSER_MISMATCH");
    }

    /// <summary>
    /// Rolls stored bytes back. Deliberately NOT cancellable: the client may already have gone, which is exactly when an
    /// orphan would otherwise be left. Retries after each of <see cref="CompensationDelays"/>, then logs ORPHAN; the
    /// sweep's reconciliation pass reclaims the file after 24 h.
    /// </summary>
    private async Task CompensateAsync(string fileKey)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await _updateServiceClient.DeleteMapFileByPathAsync(fileKey, CancellationToken.None);
                _logger.LogInformation("Compensated temporary map file {FileKey}", fileKey);
                return;
            }
            catch (Exception ex) when (attempt < CompensationDelays.Length)
            {
                _logger.LogDebug(ex, "Compensation attempt {Attempt} for {FileKey} failed; retrying", attempt + 1, fileKey);
                await WaitAsync(CompensationDelays[attempt]);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ORPHAN temporary map file left in update-service at {FileKey}", fileKey);
                return;
            }
        }
    }

    /// <summary>Logs why the stored bytes are rolled back, rolls them back, and returns the 502 to throw.</summary>
    private async Task<TemporaryMapUploadException> CompensateAsync(string fileKey, Exception cause, string why, params object[] args)
    {
        _logger.LogWarning(cause, why + "; compensating {FileKey}", [.. args, fileKey]);
        await CompensateAsync(fileKey);
        return Upstream();
    }

    /// <summary>Whole seconds, rounded up and clamped to [1, int.MaxValue] before the cast so a saturated window cannot wrap.</summary>
    internal static int RetryAfterSeconds(TimeSpan retryAfter)
        => (int)Math.Clamp(Math.Ceiling(retryAfter.TotalSeconds), 1, int.MaxValue);

    private static bool IsCancellation(Exception ex, CancellationToken cancellationToken)
        => ex is OperationCanceledException && cancellationToken.IsCancellationRequested;

    private TemporaryMapUploadException UnknownFileState(int mapId, string fileState)
        => Upstream("matchmaking answered temporary map {MapId} with fileState {FileState}, which this step cannot act on", mapId, fileState);

    /// <summary>Logs why at warning and returns the 502 to throw.</summary>
    private TemporaryMapUploadException Upstream(string why, params object[] args)
    {
        _logger.LogWarning(why, args);
        return Upstream();
    }

    private static TemporaryMapUploadException Upstream() => new(StatusCodes.Status502BadGateway, "UPSTREAM");

    private static TemporaryMapUploadException KeyMismatch() => new(StatusCodes.Status500InternalServerError, "TEMP_MAP_KEY_MISMATCH");

    private static (TemporaryMapUploadOutcome, string) Deduped(MapContract map, string sha1)
        => Outcome(TemporaryMapMetrics.Results.Deduped, map.Id, map.Path, map.Name, sha1);

    private static (TemporaryMapUploadOutcome, string) Outcome(string result, int mapId, string path, string name, string sha1) => (new TemporaryMapUploadOutcome
    {
        Created = result == TemporaryMapMetrics.Results.Created,
        Response = new TemporaryMapUploadResponse { MapId = mapId, Path = path, Name = name, Sha1 = sha1 },
    }, result);
}
