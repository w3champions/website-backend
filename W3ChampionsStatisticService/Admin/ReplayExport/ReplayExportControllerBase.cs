using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using W3C.Domain.UpdateService;

namespace W3ChampionsStatisticService.Admin.ReplayExport;

/// <summary>
/// The search, the tickets and the ZIP stream of a replay download. The admin export
/// (<see cref="ReplayExportController"/>) and the public replay store share them. Each of the two has its
/// own limit on running exports, so buyers cannot block the admins; one user runs one download at a time.
/// </summary>
public abstract class ReplayExportControllerBase(
    ReplayExportRepository repository,
    ReplayExportTicketStore tickets,
    ReplayServiceClient replayServiceClient,
    ILogger logger) : ControllerBase
{
    /// <summary>
    /// Exports of one controller that can stream at the same time, to keep the load on replay-service
    /// bounded. Each export gets its replays one after the other.
    /// </summary>
    protected const int MaxRunningExports = 2;

    /// <summary>The users with a running download, in both controllers: one user cannot take all the slots.</summary>
    private static readonly ConcurrentDictionary<string, byte> RunningUsers = new();

    /// <summary>The slowest download that keeps its slot: 16 KB/s, far below any real connection.</summary>
    private const double MinClientBytesPerSecond = 16 * 1024;

    /// <summary>The longest wait for one replay from replay-service, body included.</summary>
    private static readonly TimeSpan ReplayTimeLimit = TimeSpan.FromSeconds(60);

    private const int MaxSeasons = 100;
    private const int MaxExcludedExports = 1_000;
    private const int MaxNameLength = 200;

    protected readonly ReplayExportRepository Repository = repository;
    protected readonly ReplayExportTicketStore Tickets = tickets;
    private readonly ReplayServiceClient _replayServiceClient = replayServiceClient;
    private readonly ILogger _logger = logger;

    /// <summary>The export slots of this controller (a static semaphore of <see cref="MaxRunningExports"/>).</summary>
    protected abstract SemaphoreSlim RunningExports { get; }

    /// <summary>
    /// The download route of this controller. Its tickets download only there, so a download always takes a
    /// slot of the controller that gave the ticket.
    /// </summary>
    protected abstract string TicketScope { get; }

    /// <summary>
    /// Null when the user can start a download now, else the error. The page shows it, because the download
    /// itself is a browser navigation. The download checks again.
    /// </summary>
    protected IActionResult DownloadBusy(string battleTag)
    {
        if (RunningUsers.ContainsKey(battleTag ?? "")) return OwnDownloadRunning();
        return RunningExports.CurrentCount > 0 ? null : ExportsBusy();
    }

    /// <summary>
    /// The search: the count, the ids of the listed games and the first table page.
    /// <paramref name="onlyPurchases"/>: "exclude past downloads" means the paid purchases (the store).
    /// </summary>
    protected async Task<IActionResult> Search(ReplayExportFilter filter, string battleTag, bool onlyPurchases = false)
    {
        var error = Validate(filter);
        if (error != null) return BadRequest(new { Error = error });

        var excludedIds = await Repository.LoadExcludedIds(filter, battleTag, onlyPurchases);
        if (excludedIds.Count > ReplayExportRepository.MaxExcluded)
        {
            return BadRequest(new { Error = $"Past downloads have more than {ReplayExportRepository.MaxExcluded:N0} games. Select fewer downloads to exclude." });
        }

        try
        {
            return Ok(await Repository.Preview(filter, excludedIds));
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

    /// <summary>The games of one table page, in the order of the ids.</summary>
    protected async Task<IActionResult> TablePage(ReplayExportMatchesRequest request)
    {
        var error = ParseMatchIds(request?.MatchIds, ReplayExportRepository.MaxPageSize, $"At most {ReplayExportRepository.MaxPageSize} games per page.", out var ids);
        if (error != null) return BadRequest(new { Error = error });
        return Ok(await Repository.LoadByIds(ids));
    }

    /// <summary>
    /// The ids of a request, without duplicates: the same id in upper and lower case is one game. Returns an
    /// error text (<paramref name="tooMany"/> for more than <paramref name="max"/> ids), or null.
    /// </summary>
    protected static string ParseMatchIds(List<string> requested, int max, string tooMany, out List<ObjectId> ids)
    {
        ids = [];
        requested ??= [];
        // Before the parse, so a huge request costs nothing.
        if (requested.Count > max) return tooMany;

        var parsed = new List<ObjectId>();
        foreach (var id in requested)
        {
            if (!ObjectId.TryParse(id, out var objectId)) return $"Invalid match id: {id}";
            parsed.Add(objectId);
        }
        ids = parsed.Distinct().ToList();
        return null;
    }

    /// <summary>
    /// A repeat download of a download in the history. The server keeps no ZIP files, so this builds the ZIP
    /// again from the same games. It costs nothing: the user has the replays already.
    /// </summary>
    protected IActionResult RepeatTicket(ReplayExportRecord record, string battleTag)
    {
        if (record == null) return NotFound(new { Error = "This download is not in the history." });
        var busy = DownloadBusy(battleTag);
        if (busy != null) return busy;

        var (ticket, expiresAt) = Tickets.Create(new ReplayExportTicket
        {
            MatchIds = record.MatchIds,
            RequestedBy = battleTag,
            Label = record.Label,
            Description = record.Description,
            Filter = record.Filter,
            ExportId = record.Id.ToString(),
            Scope = TicketScope,
        });
        return Ok(new ReplayExportTicketResponse { Ticket = ticket, Count = record.MatchIds.Count, ExpiresAt = expiresAt, Price = 0 });
    }

    /// <summary>
    /// Streams the ZIP of a ticket. A plain GET (no bearer header), so the browser streams the ZIP to disk
    /// itself; the ticket authorizes it.
    /// </summary>
    protected async Task<IActionResult> StreamDownload(string ticket)
    {
        // The ticket first: a request without a valid ticket must not take an export slot.
        var pending = Tickets.Peek(ticket);
        if (pending == null || pending.Scope != TicketScope) return TicketNotFound();
        var user = pending.RequestedBy ?? "";
        if (!RunningUsers.TryAdd(user, 0)) return OwnDownloadRunning();

        try
        {
            return await StreamWithSlot(ticket);
        }
        finally
        {
            RunningUsers.TryRemove(user, out _);
        }
    }

    private async Task<IActionResult> StreamWithSlot(string ticket)
    {
        if (!await RunningExports.WaitAsync(0)) return ExportsBusy();

        try
        {
            // Null when a request with the same ticket was faster.
            var export = Tickets.Take(ticket);
            if (export == null) return TicketNotFound();

            var matches = await Repository.LoadByIds(export.MatchIds);
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
            // A client that reads very slowly must not keep an export slot for days (Kestrel's default is
            // 240 bytes/s). A download that stops can start again at no cost.
            var minRate = HttpContext.Features.Get<IHttpMinResponseDataRateFeature>();
            if (minRate != null) minRate.MinDataRate = new MinDataRate(bytesPerSecond: MinClientBytesPerSecond, gracePeriod: TimeSpan.FromSeconds(30));

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
                await Repository.MarkDownloadedAgain(exportId, export.RequestedBy);
                return;
            }

            var now = DateTime.UtcNow;
            await Repository.SaveExport(new ReplayExportRecord
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
            // The user has the ZIP already; a missing history entry must not turn that into an error.
            _logger.LogError(ex, "Could not save replay export {ExportId} to the history", exportId);
        }
    }

    /// <summary>
    /// A name or description from the user, as one line: it goes to the manifest (also the CSV) and the history.
    /// </summary>
    public static string CleanText(string text, int maxLength)
    {
        if (text == null) return null;
        var line = new string(text.Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        if (line.Length == 0) return null;
        if (line.Length <= maxLength) return line;
        // Not between the two halves of an emoji.
        var end = char.IsHighSurrogate(line[maxLength - 1]) ? maxLength - 1 : maxLength;
        return line[..end].TrimEnd();
    }

    protected IActionResult ExportsBusy() => DownloadError(StatusCodes.Status429TooManyRequests,
        "Other replay downloads are running. Try again in a few minutes.");

    private IActionResult OwnDownloadRunning() => DownloadError(StatusCodes.Status429TooManyRequests,
        "Your other replay download is still running. Start this one when it is done.");

    protected IActionResult TicketNotFound() => DownloadError(StatusCodes.Status404NotFound,
        "This download link has expired or was used already. Start the download again on the page.");

    /// <summary>
    /// The error of a download. The download is a browser navigation: the browser shows a short page, not JSON.
    /// The page's own requests get the usual JSON error.
    /// </summary>
    private IActionResult DownloadError(int status, string message)
    {
        var accept = Request.GetTypedHeaders().Accept;
        if (accept == null || !accept.Any(a => a.MediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase)))
        {
            return StatusCode(status, new { Error = message });
        }

        return new ContentResult
        {
            StatusCode = status,
            ContentType = "text/html; charset=utf-8",
            Content = "<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\">"
                + "<title>Replay download</title>"
                + "<body style=\"font-family:system-ui,sans-serif;max-width:32rem;margin:4rem auto;padding:0 16px;line-height:1.5\">"
                + "<h1 style=\"font-size:1.25rem\">The download did not start</h1>"
                + $"<p>{WebUtility.HtmlEncode(message)}</p>"
                + "<p><button type=\"button\" onclick=\"history.back()\">Go back</button></p></body></html>",
        };
    }

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

    /// <summary>The checks of a search filter. Returns an error text, or null.</summary>
    public static string Validate(ReplayExportFilter filter)
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
