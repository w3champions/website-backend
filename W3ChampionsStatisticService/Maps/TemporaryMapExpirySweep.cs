using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using W3C.Domain.Maps;
using W3C.Domain.MatchmakingService;
using W3C.Domain.MatchmakingService.Contracts;
using W3C.Domain.UpdateService;
using W3C.Domain.UpdateService.Contracts;
using static W3ChampionsStatisticService.Maps.TemporaryMapUploadRules;

namespace W3ChampionsStatisticService.Maps;

/// <summary>What one sweep did. Logged as a single summary line and returned to the admin job.</summary>
public class TemporaryMapSweepReport
{
    /// <summary>Stored CustomGames/ files examined during reconciliation.</summary>
    public int Scanned { get; internal set; }

    /// <summary>Expired maps whose file was deleted and whose record was marked deleted.</summary>
    public int Deleted { get; internal set; }

    /// <summary>Stored files no record claims any more: unclaimed, or claimed by a record that says its file is gone.</summary>
    public int ReclaimedOrphans { get; internal set; }

    /// <summary>Per-item failures; they are retried by the next run.</summary>
    public int Failed { get; internal set; }

    /// <summary>Spool files a crash or restart left behind, removed once <see cref="TemporaryMapLimits.StaleSpoolFileAgeHours"/> old.</summary>
    public int PurgedSpoolFiles { get; internal set; }
}

/// <summary>
/// Design spec §3.5. Two passes, both idempotent and both safe to re-run:
/// <list type="number">
/// <item><b>Expiry</b> — maps whose last game START is older than the TTL: delete the bytes, then mark the record. The
/// order matters: a record marked deleted while its bytes survive becomes an orphan nobody will ever reclaim by id,
/// whereas bytes deleted before the mark are simply retried. Batches are listed again while a batch was full and made
/// progress (the listing has no cursor), and every item is attempted at most once per run (S11).</item>
/// <item><b>Reconciliation</b> — stored CustomGames/ files at least <see cref="TemporaryMapLimits.OrphanMinAgeHours"/>
/// old that no record claims (a compensation that never completed), or whose record says its file is deleted (a restore
/// that never completed), are reclaimed. The listing is followed to its end; only a cursor that repeats ends the pass
/// early (I2).</item>
/// </list>
/// Before both, spool files a crash left in the upload directory are purged (Task 2 L2). Per-item failures are counted
/// and logged, never swallowed, never abort the run and are never given up on: the next run tries them again. Runs are
/// serialised on one lock (X12): the daily trigger and an admin-triggered run queue behind each other rather than
/// double-deleting. Every probe-and-delete and delete-and-mark of one fileKey is done under the per-fileKey lock the
/// upload service holds from its store to its record write (S6-M1), so neither pass can take bytes an upload has just
/// stored.
/// </summary>
public class TemporaryMapExpirySweep(
    MatchmakingServiceClient matchmakingServiceClient,
    UpdateServiceClient updateServiceClient,
    TemporaryMapFileKeyLock fileKeyLock,
    ILogger<TemporaryMapExpirySweep> logger)
{
    private static readonly TimeSpan StaleSpoolFileAge = TimeSpan.FromHours(TemporaryMapLimits.StaleSpoolFileAgeHours);

    private readonly MatchmakingServiceClient _matchmakingServiceClient = matchmakingServiceClient;
    private readonly UpdateServiceClient _updateServiceClient = updateServiceClient;
    private readonly ILogger<TemporaryMapExpirySweep> _logger = logger;

    /// <summary>One run at a time; a second caller waits rather than skips, so no trigger is ever lost.</summary>
    private readonly SemaphoreSlim _runLock = new(1, 1);

    /// <summary>
    /// The per-fileKey lock the upload service holds from its store to its record write (S6-M1): held here around every
    /// probe-and-delete and delete-and-mark of one fileKey, so a reclaim never lands on bytes an upload has just stored.
    /// </summary>
    internal TemporaryMapFileKeyLock FileKeyLock { get; } = fileKeyLock;

    /// <summary>Test seam: the spool directory; null means <see cref="TemporaryMapLimits.TempUploadDir"/>.</summary>
    internal string SpoolDirectory { get; init; }

    /// <summary>Test seam: the spool directory's file listing, so a file the purge cannot handle can be staged portably.</summary>
    internal Func<string, IEnumerable<string>> EnumerateSpoolFiles { get; init; } = Directory.EnumerateFiles;

    internal string EffectiveSpoolDirectory => SpoolDirectory ?? TemporaryMapLimits.TempUploadDir;

    /// <summary>
    /// One sweep as of <paramref name="nowUtc"/>. The clock is a parameter so the whole sweep is testable without waiting;
    /// it must be UTC, as a local or unspecified value would shift the TTL boundary by the server's zone. Virtual so the
    /// hosted service's loop can be tested against a double.
    /// </summary>
    public virtual async Task<TemporaryMapSweepReport> RunOnceAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (nowUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("must be a UTC clock", nameof(nowUtc));
        }

        await _runLock.WaitAsync(cancellationToken);
        try
        {
            var report = new TemporaryMapSweepReport();

            PurgeStaleSpoolFiles(nowUtc, report);
            await RunExpiryPass(nowUtc, report, cancellationToken);
            await RunReconciliationPass(report, cancellationToken);

            _logger.LogInformation(
                "Temporary map sweep finished: scanned={Scanned} deleted={Deleted} reclaimedOrphans={ReclaimedOrphans} failed={Failed} purgedSpoolFiles={PurgedSpoolFiles}",
                report.Scanned, report.Deleted, report.ReclaimedOrphans, report.Failed, report.PurgedSpoolFiles);

            return report;
        }
        finally
        {
            _runLock.Release();
        }
    }

    // ---- Expiry ------------------------------------------------------------------------------

    private async Task RunExpiryPass(DateTime nowUtc, TemporaryMapSweepReport report, CancellationToken cancellationToken)
    {
        var before = new DateTimeOffset(nowUtc.AddDays(-TemporaryMapLimits.TtlDays)).ToUnixTimeMilliseconds();
        var attempted = new HashSet<int>();
        bool listAgain;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<ExpiredTemporaryMap> batch;
            try
            {
                batch = (await _matchmakingServiceClient.GetExpiredTemporaryMaps(before, TemporaryMapLimits.SweepBatchSize, cancellationToken)).Items;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                report.Failed++;
                _logger.LogError(ex, "Temporary map expiry listing failed; the reconciliation pass still runs");
                return;
            }

            var deletedInBatch = 0;
            foreach (var item in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (item.Id <= 0 || !TemporaryMapKeys.IsFilePath(item.Path))
                {
                    // matchmaking drift (S6-L2): without a valid id the record could never be marked once its bytes were
                    // gone, and outside CustomGames/ the client would refuse the delete. Nothing is tried, every such row
                    // is counted, and the path is rendered only when it has the expected shape.
                    report.Failed++;
                    _logger.LogWarning("Expired temporary map row {MapId} at {FileKey} is not a deletable record; not deleted, retrying next run",
                        item.Id, LoggablePath(item.Path));
                    continue;
                }

                if (!attempted.Add(item.Id))
                {
                    // Listed again in this run: either it failed above (logged, retried next run) or matchmaking still
                    // lists a record this run marked. Neither is tried twice in one run.
                    continue;
                }

                if (await TryExpire(item, cancellationToken))
                {
                    report.Deleted++;
                    deletedInBatch++;
                }
                else
                {
                    report.Failed++;
                }
            }

            // A full batch that made progress leaves records behind that the next listing can now reach. A full batch
            // without progress would only list the same failures again; a partial batch was the last one.
            listAgain = batch.Count >= TemporaryMapLimits.SweepBatchSize && deletedInBatch > 0;
        } while (listAgain);
    }

    /// <summary>
    /// The bytes first, then the record; true once matchmaking accepted the mark. A.5 pins that answer as 200 { map }
    /// and nothing about the echoed fileState, so the echo is not inspected: a record mm did not flip is listed and
    /// marked again next run, idempotently.
    /// </summary>
    private async Task<bool> TryExpire(ExpiredTemporaryMap item, CancellationToken cancellationToken)
    {
        try
        {
            // S6-M1: an upload or restore of this fileKey holds the key from its store to its record write. Waiting comes
            // before anything is deleted, so a cancelled wait leaves nothing half-done.
            using var fileKeyHeld = await FileKeyLock.AcquireAsync(item.Path, cancellationToken);

            // Idempotent: update-service answers 204 whether or not the file was there, so a run that deleted the bytes
            // and then failed to mark the record simply does both again next time.
            await _updateServiceClient.DeleteMapFileByPathAsync(item.Path, cancellationToken);
            await _matchmakingServiceClient.MarkTemporaryMapFileDeleted(item.Id, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // An HttpClient timeout is an OperationCanceledException too; without a cancelled token it lands here as
            // the upstream failure it is. The path is matchmaking's and is only rendered when it has the expected shape.
            _logger.LogWarning(ex, "Failed to expire temporary map {MapId} at {FileKey}; retrying next run", item.Id, LoggablePath(item.Path));
            return false;
        }
    }

    // ---- Reconciliation ----------------------------------------------------------------------

    private async Task RunReconciliationPass(TemporaryMapSweepReport report, CancellationToken cancellationToken)
    {
        string after = null;
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        var scanned = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MapFileListingResponse page;
            try
            {
                page = await _updateServiceClient.ListMapFilesAsync(
                    TemporaryMapLimits.TempMapPathPrefix,
                    TemporaryMapLimits.OrphanMinAgeHours,
                    after,
                    TemporaryMapLimits.SweepBatchSize,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                report.Failed++;
                _logger.LogError(ex, "Temporary map reconciliation listing failed on page {Page}; the next run starts over", cursors.Count + 1);
                return;
            }

            var newRows = 0;
            foreach (var file in page.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!scanned.Add(file.FilePath))
                {
                    // Listed again this run (a page overlapping the previous one): examined once per run, like an expiry item.
                    continue;
                }

                newRows++;
                report.Scanned++;
                await ReconcileFile(file.FilePath, report, cancellationToken);
            }

            if (string.IsNullOrEmpty(page.Next))
            {
                return;
            }

            if (page.Files.Count > 0 && newRows == 0)
            {
                // S6-L3: a listing that ignores `after` yet mints a fresh cursor each time never repeats a cursor, so the
                // rows are the other thing that must advance. An empty page with a cursor is not this: a page can be
                // filtered down to nothing and still have more behind it. Nor is a last page that only overlaps the
                // previous one: without a cursor the pass ends here anyway.
                report.Failed++;
                _logger.LogError("Temporary map reconciliation listing repeated only rows already scanned on page {Page}; ending this run's pass, the next run starts over",
                    cursors.Count + 1);
                return;
            }

            if (!cursors.Add(page.Next))
            {
                // There is no page ceiling (I2), so a cursor that does not advance is the one way this loop could never
                // end. The cursor is update-service's opaque string and is not rendered.
                report.Failed++;
                _logger.LogError("Temporary map reconciliation listing repeated its cursor after page {Page}; ending this run's pass, the next run starts over",
                    cursors.Count);
                return;
            }

            after = page.Next;
        }
    }

    /// <summary>
    /// Keeps a file a present record claims; reclaims one that no record claims (strict 404 {}) or whose record says its
    /// file is deleted and names this very path (S-I6, S6-I1); anything else — another fileState, a deleted record that
    /// names another path, a 404 that is not the empty object, a timeout, a transport error — is this file's failure,
    /// with no delete, retried next run (I3/D2). Probe and delete happen under the fileKey lock (S6-M1): probed before
    /// an upload's record write, the answer would be "unclaimed" and the delete would take the upload's bytes.
    /// </summary>
    private async Task ReconcileFile(string filePath, TemporaryMapSweepReport report, CancellationToken cancellationToken)
    {
        if (!TemporaryMapKeys.IsFilePath(filePath))
        {
            // The listing was asked for CustomGames/ only; anything else is update-service drift and not a candidate.
            report.Failed++;
            _logger.LogWarning("Stored map listing returned {FileKey}, which is not a temporary map file; not reclaimed", LoggablePath(filePath));
            return;
        }

        try
        {
            using var fileKeyHeld = await FileKeyLock.AcquireAsync(filePath, cancellationToken);
            var claim = await _matchmakingServiceClient.GetTemporaryMapByPath(filePath, cancellationToken);
            if (claim == null)
            {
                await _updateServiceClient.DeleteMapFileByPathAsync(filePath, cancellationToken);
                report.ReclaimedOrphans++;
                _logger.LogInformation("ORPHAN_RECLAIMED {FileKey}: no temporary map claims it", LoggablePath(filePath));
                return;
            }

            switch (claim.FileState)
            {
                case TemporaryMapFileStates.Present:
                    return;
                case TemporaryMapFileStates.Deleted when !string.Equals(claim.Path, filePath, StringComparison.Ordinal):
                    // Reached by an inexact lookup: the record is about another file (S6-I1, the S-L2 precedent).
                    report.Failed++;
                    _logger.LogWarning("Temporary map {MapId} answered for {FileKey} but names {RecordFileKey}; not reclaimed, retrying next run",
                        claim.Id, LoggablePath(filePath), LoggablePath(claim.Path));
                    return;
                case TemporaryMapFileStates.Deleted:
                    await _updateServiceClient.DeleteMapFileByPathAsync(filePath, cancellationToken);
                    report.ReclaimedOrphans++;
                    _logger.LogInformation("ORPHAN_RECLAIMED {FileKey}: temporary map {MapId} says its file is deleted", LoggablePath(filePath), claim.Id);
                    return;
                default:
                    report.Failed++;
                    _logger.LogWarning("Temporary map {MapId} at {FileKey} has fileState {FileState}; not reclaimed, retrying next run",
                        claim.Id, LoggablePath(filePath), LoggableFileState(claim.FileState));
                    return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            report.Failed++;
            _logger.LogWarning(ex, "Failed to reconcile stored map file {FileKey}; retrying next run", LoggablePath(filePath));
        }
    }

    // ---- Spool purge -------------------------------------------------------------------------

    /// <summary>
    /// Removes spool files not written for <see cref="TemporaryMapLimits.StaleSpoolFileAgeHours"/> as of the run's clock.
    /// A live upload's file is minutes old at most. On Linux an unlinked open file does not break its writer. Runs
    /// first, so an upstream outage never delays it; per file, so one failure never hides the rest; and whatever it
    /// throws is this run's failure, never the passes' (R-Minor5). The directory must pass the reader's rules
    /// (<see cref="TemporaryMapUploadReader.RefusalOf"/>): a purge through a link would delete elsewhere (S6-L1).
    /// </summary>
    private void PurgeStaleSpoolFiles(DateTime nowUtc, TemporaryMapSweepReport report)
    {
        var staleBefore = nowUtc - StaleSpoolFileAge;
        try
        {
            if (!Directory.Exists(EffectiveSpoolDirectory))
            {
                return;
            }

            var directory = TemporaryMapUploadReader.ResolveSpoolDirectory(EffectiveSpoolDirectory);
            var refusal = TemporaryMapUploadReader.RefusalOf(directory, out var cause);
            if (refusal != null)
            {
                // The path is a server-chosen directory: no proof, token or client data.
                report.Failed++;
                _logger.LogError(cause, "Stale temporary map spool files were not purged from {SpoolPath}: {SpoolFault}", directory, refusal);
                return;
            }

            foreach (var path in EnumerateSpoolFiles(directory))
            {
                if (string.Equals(Path.GetExtension(path), TemporaryMapUploadReader.SpoolFileExtension, StringComparison.Ordinal))
                {
                    PurgeIfStale(path, staleBefore, report);
                }
            }
        }
        catch (Exception ex)
        {
            report.Failed++;
            _logger.LogError(ex, "Stale temporary map spool files could not be listed; retrying next run");
        }
    }

    private void PurgeIfStale(string path, DateTime staleBefore, TemporaryMapSweepReport report)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.LastWriteTimeUtc > staleBefore)
            {
                return;
            }

            // S6-L4: read before the delete; afterwards the cached status is refreshed from a file that is gone.
            var lastWriteUtc = file.LastWriteTimeUtc;
            file.Delete();
            report.PurgedSpoolFiles++;
            _logger.LogInformation("Purged a stale temporary map spool file last written {LastWriteUtc}",
                lastWriteUtc.ToString("o", CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            // Whatever the fault, it is this file's; the name is rendered only when it has the reader's spool-file shape.
            report.Failed++;
            _logger.LogWarning(ex, "Failed to purge the temporary map spool file {SpoolFile}; retrying next run", LoggableSpoolFileName(path));
        }
    }
}
