using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using W3C.Contracts.Admin.Permission;
using W3C.Contracts.Matchmaking;
using W3C.Domain.Tracing;
using W3C.Domain.UpdateService;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.Admin.ReplayExport;

/// <summary>
/// Admin bulk download of replays. Mongo (Matchup) selects the games; replay-service builds each .w3g from
/// the FLO archive in S3, the same way as the public single-replay download. So this service needs no S3
/// credentials.
/// </summary>
[ApiController]
[Route("api/admin/replay-export")]
[Trace]
public class ReplayExportController(
    ReplayExportRepository repository,
    ReplayExportTicketStore tickets,
    ReplayServiceClient replayServiceClient,
    ILogger<ReplayExportController> logger) : ControllerBase
{
    /// <summary>Exports that can stream at the same time, to keep the load on replay-service bounded.</summary>
    private const int MaxRunningExports = 2;

    private static readonly SemaphoreSlim RunningExports = new(MaxRunningExports, MaxRunningExports);

    /// <summary>The longest wait for one replay from replay-service, body included.</summary>
    private static readonly TimeSpan ReplayTimeLimit = TimeSpan.FromSeconds(60);

    private const int MaxSeasons = 100;
    private const int MaxExcludedExports = 1_000;
    private const int MaxNameLength = 200;

    private readonly ReplayExportRepository _repository = repository;
    private readonly ReplayExportTicketStore _tickets = tickets;
    private readonly ReplayServiceClient _replayServiceClient = replayServiceClient;
    private readonly ILogger<ReplayExportController> _logger = logger;

    /// <summary>The past downloads of the acting user (<paramref name="battleTag"/>). Each user has their own history.</summary>
    [HttpGet("exports")]
    [BearerHasPermissionFilter(Permission = EPermission.Replays)]
    public async Task<IActionResult> GetExports(string battleTag)
    {
        return Ok(await _repository.LoadExports(battleTag));
    }

    /// <summary>For admins: the downloads of all users, or of one <paramref name="user"/>.</summary>
    [HttpGet("all-exports")]
    [BearerHasPermissionFilter(Permission = EPermission.Replays)]
    public async Task<IActionResult> GetAllExports([FromQuery] string user)
    {
        return Ok(await _repository.LoadAllExports(user));
    }

    [HttpGet("options")]
    [BearerHasPermissionFilter(Permission = EPermission.Replays)]
    public async Task<IActionResult> GetOptions([FromQuery] GameMode gameMode = GameMode.GM_1v1)
    {
        return Ok(new ReplayExportOptions
        {
            Patches = await _repository.LoadPatchOptions(),
            Seasons = await _repository.LoadSeasons(),
            MapNames = await _repository.LoadMapNames(gameMode),
            MaxQuantity = ReplayExportRepository.MaxQuantity,
            Prices = new ReplayExportPrices(),
        });
    }

    [HttpPost("preview")]
    [BearerHasPermissionFilter(Permission = EPermission.Replays)]
    public async Task<IActionResult> Preview([FromBody] ReplayExportFilter filter, string battleTag)
    {
        var error = Validate(filter);
        if (error != null) return BadRequest(new { Error = error });

        var excludedIds = await _repository.LoadExcludedIds(filter, battleTag);
        if (excludedIds.Count > ReplayExportRepository.MaxExcluded)
        {
            return BadRequest(new { Error = $"Past downloads have more than {ReplayExportRepository.MaxExcluded:N0} games. Select fewer downloads to exclude." });
        }

        try
        {
            return Ok(await _repository.Preview(filter, excludedIds));
        }
        catch (MongoExecutionTimeoutException)
        {
            _logger.LogWarning("Replay export search by {BattleTag} hit the time limit: {@Filter}", battleTag, filter);
            return StatusCode(StatusCodes.Status422UnprocessableEntity, new
            {
                Error = "The search took too long. Select a season, a patch or dates, and try again.",
            });
        }
    }

    /// <summary>
    /// The games of one table page, in the order of the ids. A search gives only the ids of the listed games
    /// (see <see cref="ReplayExportPreview"/>), so the page gets each table page when it shows it.
    /// </summary>
    [HttpPost("matches")]
    [BearerHasPermissionFilter(Permission = EPermission.Replays)]
    public async Task<IActionResult> LoadMatches([FromBody] ReplayExportMatchesRequest request)
    {
        var requested = request?.MatchIds ?? [];
        if (requested.Count > ReplayExportRepository.MaxPageSize)
        {
            return BadRequest(new { Error = $"At most {ReplayExportRepository.MaxPageSize} games per page." });
        }

        var ids = new List<ObjectId>();
        foreach (var id in requested)
        {
            if (!ObjectId.TryParse(id, out var objectId)) return BadRequest(new { Error = $"Invalid match id: {id}" });
            ids.Add(objectId);
        }
        return Ok(await _repository.LoadByIds(ids));
    }

    /// <summary>
    /// Step 1 of a download: an authenticated request turns the selected games into a single-use ticket.
    /// <paramref name="battleTag"/> is the acting admin, set by <see cref="BearerHasPermissionFilter"/>.
    /// </summary>
    [HttpPost("tickets")]
    [BearerHasPermissionFilter(Permission = EPermission.Replays)]
    public IActionResult CreateTicket([FromBody] ReplayExportTicketRequest request, string battleTag)
    {
        var requested = request?.MatchIds ?? [];
        if (requested.Count > ReplayExportRepository.MaxQuantity)
        {
            return BadRequest(new { Error = $"At most {ReplayExportRepository.MaxQuantity:N0} replays per download." });
        }

        var parsed = new List<ObjectId>();
        foreach (var id in requested)
        {
            if (!ObjectId.TryParse(id, out var objectId)) return BadRequest(new { Error = $"Invalid match id: {id}" });
            parsed.Add(objectId);
        }

        // After the parse: the same id in upper and lower case is one game.
        var ids = parsed.Distinct().ToList();
        if (ids.Count == 0) return BadRequest(new { Error = "Select at least one game." });

        // Checked here too, so the page can show the error: the download itself is a browser navigation.
        if (RunningExports.CurrentCount == 0) return ExportsBusy();

        var (ticket, expiresAt) = _tickets.Create(new ReplayExportTicket
        {
            MatchIds = ids,
            RequestedBy = battleTag,
            Label = CleanText(request.Label, 100),
            Description = CleanText(request.Description, 500),
            // Only for the manifest and the history. A filter that a search would not accept is not kept.
            Filter = request.Filter != null && Validate(request.Filter) == null ? request.Filter : null,
        });
        return Ok(new ReplayExportTicketResponse
        {
            Ticket = ticket,
            Count = ids.Count,
            ExpiresAt = expiresAt,
            Price = ReplayExportPricing.Price(ids.Count),
            // Admins download for free. A public download must take the payment before it creates the ticket.
            Charged = false,
        });
    }

    /// <summary>
    /// A repeat download of a download in the history. The server keeps no ZIP files, so this builds the ZIP
    /// again from the same games. <paramref name="battleTag"/> is the acting admin.
    /// </summary>
    [HttpPost("exports/{exportId}/tickets")]
    [BearerHasPermissionFilter(Permission = EPermission.Replays)]
    public async Task<IActionResult> CreateRepeatTicket([FromRoute] string exportId, string battleTag)
    {
        return RepeatTicket(await _repository.LoadExport(exportId, battleTag), battleTag);
    }

    /// <summary>For admins: a repeat download of any user's download. It shows in that download's history.</summary>
    [HttpPost("all-exports/{exportId}/tickets")]
    [BearerHasPermissionFilter(Permission = EPermission.Replays)]
    public async Task<IActionResult> CreateAdminRepeatTicket([FromRoute] string exportId, string battleTag)
    {
        return RepeatTicket(await _repository.LoadAnyExport(exportId), battleTag);
    }

    private IActionResult RepeatTicket(ReplayExportRecord record, string battleTag)
    {
        if (record == null) return NotFound(new { Error = "This download is not in the history." });
        if (RunningExports.CurrentCount == 0) return ExportsBusy();

        var (ticket, expiresAt) = _tickets.Create(new ReplayExportTicket
        {
            MatchIds = record.MatchIds,
            RequestedBy = battleTag,
            Label = record.Label,
            Description = record.Description,
            Filter = record.Filter,
            ExportId = record.Id.ToString(),
        });
        // The user has the replays already: a repeat download costs nothing.
        return Ok(new ReplayExportTicketResponse { Ticket = ticket, Count = record.MatchIds.Count, ExpiresAt = expiresAt, Price = 0 });
    }

    /// <summary>
    /// Step 2 of a download: a plain GET (no bearer header), so the browser streams the ZIP to disk itself.
    /// The ticket from step 1 authorizes it.
    /// </summary>
    [HttpGet("download/{ticket}")]
    public async Task<IActionResult> Download([FromRoute] string ticket)
    {
        // The ticket first: a request without a valid ticket must not take an export slot.
        if (!_tickets.Exists(ticket)) return TicketNotFound();
        if (!await RunningExports.WaitAsync(0)) return ExportsBusy();

        try
        {
            var export = _tickets.Take(ticket);
            if (export == null) return TicketNotFound();

            var matches = await _repository.LoadByIds(export.MatchIds);
            var fileName = $"w3c-replays-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{matches.Count}.zip";
            _logger.LogInformation("Replay export of {Count} games started by {BattleTag}", matches.Count, export.RequestedBy);

            Response.ContentType = "application/zip";
            // nginx must pass the ZIP through as it streams. With buffering, it writes up to 1 GB per
            // download to a temporary file on its disk.
            Response.Headers["X-Accel-Buffering"] = "no";
            Response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";
            // ZipArchive writes synchronously, and Kestrel refuses synchronous writes by default.
            var bodyControl = HttpContext.Features.Get<IHttpBodyControlFeature>();
            if (bodyControl != null) bodyControl.AllowSynchronousIO = true;

            var exportId = export.ExportId != null ? ObjectId.Parse(export.ExportId) : ObjectId.GenerateNewId();
            var summary = await ReplayArchiveWriter.Write(
                Response.Body,
                matches,
                FetchReplay,
                new ReplayArchiveInfo { ExportId = exportId.ToString(), Label = export.Label, Filter = export.Filter?.ForManifest() },
                HttpContext.RequestAborted);

            _logger.LogInformation(
                "Replay export {ExportId} by {BattleTag} finished: {Written} replays, {Failed} failed",
                exportId, export.RequestedBy, summary.Written, summary.Failed);
            await SaveToHistory(exportId, export, matches.Count, summary);
            return new EmptyResult();
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogInformation("Replay export cancelled by the client");
            return new EmptyResult();
        }
        finally
        {
            RunningExports.Release();
        }
    }

    /// <summary>
    /// Only a finished ZIP goes into the history. A cancelled download can be done again. A repeat download
    /// only counts.
    /// </summary>
    private async Task SaveToHistory(ObjectId exportId, ReplayExportTicket export, int requested, ReplayArchiveSummary summary)
    {
        if (summary.Written == 0) return;
        try
        {
            if (export.ExportId != null)
            {
                await _repository.MarkDownloadedAgain(exportId, export.RequestedBy);
                return;
            }

            var now = DateTime.UtcNow;
            await _repository.SaveExport(new ReplayExportRecord
            {
                Id = exportId,
                CreatedAt = now,
                LastDownloadedAt = now,
                Downloads = [new ReplayExportDownload { By = export.RequestedBy, At = now }],
                RequestedBy = export.RequestedBy,
                Label = export.Label,
                Description = export.Description,
                Requested = requested,
                MatchIds = summary.WrittenMatchIds.Select(ObjectId.Parse).ToList(),
                Failed = summary.Failed,
                Filter = export.Filter,
                Price = ReplayExportPricing.Price(summary.Written),
                Charged = false,
            });
        }
        catch (Exception ex)
        {
            // The admin has the ZIP already; a missing history entry must not turn that into an error.
            _logger.LogError(ex, "Could not save replay export {ExportId} to the history", exportId);
        }
    }

    private static string CleanText(string text, int maxLength)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }

    private ObjectResult ExportsBusy() => StatusCode(StatusCodes.Status429TooManyRequests,
        new { Error = "Other replay exports are running. Try again when they finish." });

    private NotFoundObjectResult TicketNotFound() => NotFound(new { Error = "This download link has expired or was used already." });

    /// <summary>
    /// One replay. The time limit covers the body too: the HttpClient timeout stops at the headers, and one
    /// stalled body would stop the whole ZIP. The archive writer reports a time-out as "timeout".
    /// </summary>
    private async Task<byte[]> FetchReplay(int floMatchId, CancellationToken cancellationToken)
    {
        using var timeLimit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeLimit.CancelAfter(ReplayTimeLimit);
        await using var replay = await _replayServiceClient.GenerateReplay(floMatchId, timeLimit.Token);
        if (replay is MemoryStream memory) return memory.ToArray();

        using var bytes = new MemoryStream();
        await replay.CopyToAsync(bytes, timeLimit.Token);
        return bytes.ToArray();
    }

    private static string Validate(ReplayExportFilter filter)
    {
        if (filter == null) return "Missing filter.";
        if (filter.Quantity < 1 || filter.Quantity > ReplayExportRepository.MaxQuantity)
        {
            return $"Quantity must be between 1 and {ReplayExportRepository.MaxQuantity:N0}.";
        }
        if (filter.MinMmr > filter.MaxMmr) return "Min MMR is greater than max MMR.";
        if (filter.MinDurationSeconds > filter.MaxDurationSeconds) return "Min duration is greater than max duration.";
        if (filter.From >= filter.To) return "The start date must be before the end date.";
        if (filter.Seasons?.Count > MaxSeasons) return $"Select at most {MaxSeasons} seasons.";
        if (filter.ExcludeExportIds?.Count > MaxExcludedExports) return $"Select at most {MaxExcludedExports:N0} past downloads to exclude.";
        if (filter.BattleTag?.Length > MaxNameLength || filter.MapName?.Length > MaxNameLength) return "The player or the map is too long.";
        if (filter.BattleTag?.Contains('\0') == true || filter.MapName?.Contains('\0') == true) return "The player or the map has an invalid character.";
        return null;
    }
}
