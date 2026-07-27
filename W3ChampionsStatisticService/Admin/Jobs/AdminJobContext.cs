using System;
using System.Diagnostics;
using System.Threading;
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
public class AdminJobContext(string key, IAdminJobRepository repository, AdminJob job, double maxDutyCycle)
    : IAdminJobContext
{
    public static readonly TimeSpan WriteInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Ceiling on a single pause, so one pathological batch cannot stall the job for
    /// hours and make it look hung.
    /// </summary>
    public static readonly TimeSpan MaxPause = TimeSpan.FromSeconds(30);

    private readonly double _dutyCycle = Math.Clamp(maxDutyCycle, 0.01, 1.0);
    private readonly AdminJobProgress _progress = job.Progress ?? new AdminJobProgress();
    private readonly Stopwatch _sinceLastWrite = Stopwatch.StartNew();
    private readonly Stopwatch _sinceLastPace = Stopwatch.StartNew();
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
    /// Sleeps for however long it takes to bring the job back under its duty cycle,
    /// measuring from the last call - so at 25%, a batch that took one second is
    /// followed by three seconds of quiet.
    /// <para>
    /// This paces against observed latency rather than any server-side health metric,
    /// which is deliberate: when the database is under pressure - from this job or
    /// anything else - batches take longer, so the pauses grow in step without the
    /// runner needing to interpret Mongo's internals or know the deployment topology.
    /// A fixed inter-batch delay does the opposite, running hardest exactly when the
    /// server is coping best and never backing off when it is struggling.
    /// </para>
    /// </summary>
    public async Task Pace(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var busy = _sinceLastPace.Elapsed;
        var pause = _dutyCycle >= 1.0
            ? TimeSpan.Zero
            : busy * ((1.0 / _dutyCycle) - 1.0);

        if (pause > TimeSpan.Zero)
        {
            await Task.Delay(pause < MaxPause ? pause : MaxPause, cancellationToken);
        }

        _sinceLastPace.Restart();
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
