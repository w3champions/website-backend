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
public class AdminJobContext(
    string key,
    IAdminJobRepository repository,
    AdminJob job,
    double fallbackDutyCycle,
    IPressureProbe pressureProbe) : IAdminJobContext
{
    public static readonly TimeSpan WriteInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Ceiling on a single pause, so one pathological batch cannot stall the job for
    /// hours and make it look hung.
    /// </summary>
    public static readonly TimeSpan MaxPause = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How often the pressure signals are re-read. Sampling costs a serverStatus round
    /// trip, and these signals do not move meaningfully faster than this.
    /// </summary>
    public static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Thresholds at which a signal counts as pressure. Deliberately named rather than
    /// inlined: they are judgement calls and will want tuning against real load.
    /// </summary>
    private const double DirtyCacheLimit = 0.10;         // WiredTiger panics at 0.20
    private const double WriteTicketLimit = 0.50;
    private const long QueuedWriterLimit = 2;
    private const double ProcessCpuLimit = 0.70;         // leave headroom for API traffic

    /// <summary>Pause length as a multiple of the last batch, while pressure persists.</summary>
    private const double MaxBackoff = 32;

    private readonly double _fallbackDutyCycle = Math.Clamp(fallbackDutyCycle, 0.01, 1.0);
    private readonly AdminJobProgress _progress = job.Progress ?? new AdminJobProgress();
    private readonly Stopwatch _sinceLastWrite = Stopwatch.StartNew();
    private readonly Stopwatch _sinceLastPace = Stopwatch.StartNew();
    private BsonDocument _pendingCheckpoint;
    private bool _dirty;

    private PressureSample _lastSample;
    private double _backoff;

    /// <summary>
    /// False until proven otherwise, so a job paces conservatively for its first second
    /// rather than sprinting before the first reading lands.
    /// </summary>
    private bool _databaseAvailable;

    /// <summary>Why the job last slowed down, surfaced in the progress message.</summary>
    public string LastPressureReason { get; private set; }

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
    /// Runs the job as fast as the database and this service will tolerate, pausing only
    /// when something says to slow down.
    /// <para>
    /// A fixed duty cycle is the fallback, not the plan: it makes every job slow whether
    /// or not that is needed, and picking the number is guesswork. When the pressure
    /// signals are readable the job runs flat out and backs off on evidence instead -
    /// growing the pause while pressure persists and decaying it once things recover.
    /// If the database signal cannot be read at all the fixed pace comes back, because
    /// running flat out blind is the one genuinely dangerous option.
    /// </para>
    /// </summary>
    public async Task Pace(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var busy = _sinceLastPace.Elapsed;

        // Only re-evaluate when there is a fresh reading. Paces in between keep the
        // current backoff: decaying on every pace would let a job outrun the sampling
        // interval and talk itself out of a backoff it still needs.
        var delta = await TakeSample(cancellationToken);
        if (delta != null)
        {
            var reason = Evaluate(delta);

            // Doubling on pressure and halving on relief reacts within a couple of
            // samples without thrashing between flat out and stopped.
            _backoff = reason != null
                ? Math.Min(Math.Max(_backoff * 2, 1), MaxBackoff)
                : _backoff / 2;

            if (_backoff < 0.25)
            {
                _backoff = 0;
            }

            LastPressureReason = reason;
        }

        // Without a database signal, hold the job to a fixed share of wall clock
        // instead. The CPU brake still applies on top of it.
        var blindPace = _databaseAvailable || _fallbackDutyCycle >= 1.0
            ? TimeSpan.Zero
            : busy * ((1.0 / _fallbackDutyCycle) - 1.0);

        var pause = Max(busy * _backoff, blindPace);
        if (pause > MaxPause)
        {
            pause = MaxPause;
        }

        if (pause > TimeSpan.Zero)
        {
            await Task.Delay(pause, cancellationToken);
        }

        _sinceLastPace.Restart();
    }

    /// <summary>
    /// Returns the change since the previous reading, or null if it is too soon to
    /// sample again or this is the first reading - counters need a baseline before a
    /// difference means anything.
    /// </summary>
    private async Task<PressureDelta> TakeSample(CancellationToken cancellationToken)
    {
        if (_lastSample != null && DateTimeOffset.UtcNow - _lastSample.TakenAt < SampleInterval)
        {
            return null;
        }

        var sample = await pressureProbe.Sample(cancellationToken);
        var previous = _lastSample;

        _lastSample = sample;
        _databaseAvailable = sample.DatabaseAvailable;

        if (previous == null)
        {
            return null;
        }

        var wallSeconds = (sample.TakenAt - previous.TakenAt).TotalSeconds;
        var cpuSeconds = (sample.ProcessCpuTime - previous.ProcessCpuTime).TotalSeconds;

        return new PressureDelta(
            DirtyTriggerReached: sample.DirtyTriggerReached - previous.DirtyTriggerReached,
            ApplicationThreadEvictions: sample.ApplicationThreadEvictions - previous.ApplicationThreadEvictions,
            DirtyCacheFraction: sample.DirtyCacheFraction,
            WriteTicketUtilisation: sample.WriteTicketUtilisation,
            QueuedWriters: sample.QueuedWriters,
            CpuFraction: wallSeconds > 0 ? cpuSeconds / (wallSeconds * sample.ProcessorCount) : 0);
    }

    /// <summary>Counters as rates and gauges as they came, ready to threshold.</summary>
    private sealed record PressureDelta(
        long DirtyTriggerReached,
        long ApplicationThreadEvictions,
        double DirtyCacheFraction,
        double WriteTicketUtilisation,
        long QueuedWriters,
        double CpuFraction);

    /// <summary>Returns why the job should slow down, or null if nothing objects.</summary>
    private static string Evaluate(PressureDelta delta)
    {
        // mongod forcing its own query threads to evict, or hitting the dirty trigger,
        // means writes are outrunning what it can absorb. These are the strongest
        // signals available on a standalone server, and being counters they need no
        // threshold - any movement at all is the server complaining.
        if (delta.ApplicationThreadEvictions > 0)
        {
            return "mongod is evicting on application threads";
        }

        if (delta.DirtyTriggerReached > 0)
        {
            return "mongod hit its dirty cache trigger";
        }

        if (delta.DirtyCacheFraction > DirtyCacheLimit)
        {
            return $"dirty cache at {delta.DirtyCacheFraction:P0}";
        }

        if (delta.WriteTicketUtilisation > WriteTicketLimit)
        {
            return $"write tickets {delta.WriteTicketUtilisation:P0} used";
        }

        if (delta.QueuedWriters >= QueuedWriterLimit)
        {
            return $"{delta.QueuedWriters} writers queued";
        }

        // Jobs share a process with the API, so a pegged CPU degrades the site even when
        // the database is perfectly happy.
        return delta.CpuFraction > ProcessCpuLimit ? $"service CPU at {delta.CpuFraction:P0}" : null;
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

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
