using System;
using System.Collections.Generic;
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
/// double-deleting.
/// </summary>
public class TemporaryMapExpirySweep(
    MatchmakingServiceClient matchmakingServiceClient,
    UpdateServiceClient updateServiceClient,
    ILogger<TemporaryMapExpirySweep> logger)
{
    private static readonly TimeSpan StaleSpoolFileAge = TimeSpan.FromHours(TemporaryMapLimits.StaleSpoolFileAgeHours);

    private readonly MatchmakingServiceClient _matchmakingServiceClient = matchmakingServiceClient;
    private readonly UpdateServiceClient _updateServiceClient = updateServiceClient;
    private readonly ILogger<TemporaryMapExpirySweep> _logger = logger;

    /// <summary>One run at a time; a second caller waits rather than skips, so no trigger is ever lost.</summary>
    private readonly SemaphoreSlim _runLock = new(1, 1);

    /// <summary>Test seam: the spool directory; null means <see cref="TemporaryMapLimits.TempUploadDir"/>.</summary>
    internal string SpoolDirectory { get; init; }

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

    /// <summary>The bytes first, then the record; true only when the record says deleted.</summary>
    private async Task<bool> TryExpire(ExpiredTemporaryMap item, CancellationToken cancellationToken)
    {
        try
        {
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

            foreach (var file in page.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                report.Scanned++;
                await ReconcileFile(file.FilePath, report, cancellationToken);
            }

            if (string.IsNullOrEmpty(page.Next))
            {
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
    /// file is deleted (S-I6); anything else — another fileState, a 404 that is not the empty object, a timeout, a
    /// transport error — is this file's failure, with no delete, retried next run (I3/D2).
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
    /// first, so an upstream outage never delays it, and per file, so one failure never hides the rest.
    /// </summary>
    private void PurgeStaleSpoolFiles(DateTime nowUtc, TemporaryMapSweepReport report)
    {
        var directory = EffectiveSpoolDirectory;
        var staleBefore = nowUtc - StaleSpoolFileAge;
        try
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            foreach (var path in Directory.EnumerateFiles(directory))
            {
                if (string.Equals(Path.GetExtension(path), TemporaryMapUploadReader.SpoolFileExtension, StringComparison.Ordinal))
                {
                    PurgeIfStale(path, staleBefore, report);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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

            file.Delete();
            report.PurgedSpoolFiles++;
            _logger.LogInformation("Purged a stale temporary map spool file last written {LastWriteUtc}", file.LastWriteTimeUtc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            report.Failed++;
            _logger.LogWarning(ex, "Failed to purge a stale temporary map spool file; retrying next run");
        }
    }
}
