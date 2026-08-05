using System;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using W3C.Domain.Repositories;

namespace W3ChampionsStatisticService.Admin.Jobs;

public enum AdminJobStatus
{
    /// <summary>Never run, or the state has been reset.</summary>
    Idle,
    Running,
    Completed,
    Failed,
    /// <summary>An admin cancelled it. Resumable from its checkpoint.</summary>
    Cancelled,
    /// <summary>
    /// The process died while it was running (deploy, crash, OOM). Detected at
    /// startup rather than by heartbeat - see <see cref="AdminJobRunner"/>.
    /// </summary>
    Interrupted,
}

/// <summary>
/// Current state of one job, keyed by <see cref="IAdminJob.Key"/>. This is state,
/// not a run log: a re-run overwrites the previous run's details. See
/// docs/admin-job-runner.md for why there is no run-history collection.
/// </summary>
public class AdminJob : IIdentifiable
{
    [BsonId]
    public string Id { get; set; }

    /// <summary>
    /// Stored as a string rather than the codebase-default ordinal. This collection
    /// exists to be read by a human debugging a stuck job, and it means reordering
    /// the enum can't silently reinterpret existing documents.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public AdminJobStatus Status { get; set; } = AdminJobStatus.Idle;

    public AdminJobProgress Progress { get; set; } = new();

    /// <summary>
    /// Job-defined resume point, opaque to the runner. Cleared only by an explicit
    /// reset - a failed or cancelled run keeps it so the next run continues.
    /// </summary>
    public BsonDocument Checkpoint { get; set; }

    /// <summary>
    /// Start of the current leg. Reset on every resume, so this is not when the run as
    /// a whole began - <see cref="DurationMs"/> is the figure to display.
    /// </summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>Set on every terminal status, not just <see cref="AdminJobStatus.Completed"/>.</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>
    /// Time this run spent actually working in legs before the current one. A job that
    /// is stopped and resumed - including "paused", which is cancel plus run - would
    /// otherwise report all the idle time in between as runtime.
    /// </summary>
    public long ActiveMs { get; set; }

    /// <summary>
    /// Total time worked across every leg of the current or most recent run, excluding
    /// time spent stopped. Denormalised so the UI does not recompute it.
    /// <para>
    /// Milliseconds rather than a <see cref="TimeSpan"/> deliberately: the driver
    /// serialises TimeSpan as a formatted string, and its numeric representation is
    /// ticks, which has already caused unit confusion elsewhere in this collection's
    /// neighbourhood. A plain number of milliseconds is unambiguous in the database
    /// and directly usable in JSON.
    /// </para>
    /// </summary>
    public long? DurationMs { get; set; }

    /// <summary>Job-defined unit (matches, players, ...). Accumulates across resumes.</summary>
    public long ItemsProcessed { get; set; }

    /// <summary>BattleTag of the admin who triggered the current or most recent run.</summary>
    public string TriggeredBy { get; set; }

    public int RunCount { get; set; }

    public string Error { get; set; }
}

public class AdminJobProgress
{
    public long Current { get; set; }

    /// <summary>Zero when the job cannot know the total up front.</summary>
    public long Total { get; set; }

    public string Message { get; set; }
}
