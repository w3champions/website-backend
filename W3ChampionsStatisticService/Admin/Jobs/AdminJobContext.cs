using System;
using System.Diagnostics;
using System.Threading.Tasks;
using MongoDB.Bson;

namespace W3ChampionsStatisticService.Admin.Jobs;

/// <summary>
/// Buffers a job's progress and writes it at most once per <see cref="WriteInterval"/>.
/// <para>
/// Throttling lives here rather than in each job so that a job reporting per record
/// does not spend more time writing progress than working. The cost is that an abrupt
/// death can lose up to one interval of checkpoint progress, so jobs must be safe to
/// redo the work since their last checkpoint - which resumability already requires.
/// </para>
/// </summary>
public class AdminJobContext(string key, IAdminJobRepository repository, AdminJob job) : IAdminJobContext
{
    public static readonly TimeSpan WriteInterval = TimeSpan.FromSeconds(2);

    private readonly AdminJobProgress _progress = job.Progress ?? new AdminJobProgress();
    private readonly Stopwatch _sinceLastWrite = Stopwatch.StartNew();
    private BsonDocument _pendingCheckpoint;
    private bool _dirty;

    public BsonDocument Checkpoint { get; } = job.Checkpoint;

    public long ItemsProcessed { get; private set; } = job.ItemsProcessed;

    public Task Report(long current, long total, string message = null, BsonDocument checkpoint = null)
    {
        _progress.Current = current;
        _progress.Total = total;
        _progress.Message = message ?? _progress.Message;
        _pendingCheckpoint = checkpoint ?? _pendingCheckpoint;
        _dirty = true;

        return _sinceLastWrite.Elapsed < WriteInterval ? Task.CompletedTask : Flush();
    }

    public void AddItems(long count)
    {
        ItemsProcessed += count;
        _dirty = true;
    }

    /// <summary>
    /// Writes buffered progress regardless of the throttle. The runner calls this once
    /// the job returns - however it returns - so the final state and the last reported
    /// checkpoint are always persisted.
    /// </summary>
    public async Task Flush()
    {
        if (!_dirty)
        {
            return;
        }

        // Reset before the write, not after: a slow write should not immediately owe
        // another one.
        _sinceLastWrite.Restart();
        _dirty = false;

        var checkpoint = _pendingCheckpoint;
        _pendingCheckpoint = null;

        try
        {
            await repository.ReportProgress(key, _progress, checkpoint, ItemsProcessed);
        }
        catch
        {
            // Put the checkpoint back so a transient write failure doesn't silently
            // cost the job its resume point. The job itself will fail on this.
            _pendingCheckpoint ??= checkpoint;
            _dirty = true;
            throw;
        }
    }
}
