using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using W3C.Contracts.Admin.Permission;

namespace W3ChampionsStatisticService.Admin.Jobs;

/// <summary>
/// A manually-triggered operational job. Register implementations with
/// <c>AddInterceptedScoped&lt;IAdminJob, MyJob&gt;()</c>; the runner discovers them from DI
/// the same way <c>IRequiresIndexes</c> repositories are discovered.
/// <para>
/// Implementations must be resumable: a job may be started again after being
/// interrupted, cancelled or failed, and is handed back the checkpoint it last
/// reported. Up to a few seconds of checkpoint progress can be lost on an abrupt
/// death (see <see cref="AdminJobContext"/>), so work between checkpoints must be
/// safe to redo.
/// </para>
/// </summary>
public interface IAdminJob
{
    /// <summary>Stable identifier, used as the document id and in the API route.</summary>
    string Key { get; }

    string Name { get; }

    string Description { get; }

    /// <summary>
    /// Checked in addition to <see cref="EPermission.Jobs"/>, which gates the jobs
    /// API as a whole. Lets an individual job be restricted further without changing
    /// the runner.
    /// </summary>
    EPermission RequiredPermission => EPermission.Jobs;

    /// <summary>When true the UI makes the admin type the job name before running it.</summary>
    bool RequiresConfirmation => false;

    /// <summary>
    /// The largest share of wall-clock time this job may spend working, enforced by
    /// <see cref="IAdminJobContext.Pace"/>. 1.0 lets it run flat out; the default leaves
    /// the database idle three quarters of the time.
    /// <para>
    /// Only has an effect if the job actually calls <c>Pace</c> between batches.
    /// </para>
    /// </summary>
    double MaxDutyCycle => 0.25;

    Task RunAsync(IAdminJobContext context, CancellationToken cancellationToken);
}

/// <summary>
/// How a job reads its resume point and reports progress back. Writes are throttled
/// by the implementation, so jobs may call <see cref="Report"/> freely.
/// </summary>
public interface IAdminJobContext
{
    /// <summary>
    /// The checkpoint reported by the previous run, or null on a fresh run. The job
    /// defines the shape.
    /// </summary>
    BsonDocument Checkpoint { get; }

    /// <summary>Count carried over from a previous run, so a resumed job keeps counting up.</summary>
    long ItemsProcessed { get; }

    /// <summary>
    /// Records progress. Returns a completed task when the write is throttled away,
    /// so this is cheap to call in a tight loop, but it must still be awaited - it
    /// performs the write when the throttle interval has elapsed.
    /// </summary>
    /// <param name="total">Zero if unknown.</param>
    /// <param name="checkpoint">
    /// When supplied, replaces the resume point. Persisted with the next write, which
    /// may not be this call.
    /// </param>
    Task Report(long current, long total, string message = null, BsonDocument checkpoint = null);

    /// <summary>Adds to <see cref="ItemsProcessed"/>.</summary>
    void AddItems(long count);

    /// <summary>
    /// Call between batches to keep the job within its <see cref="IAdminJob.MaxDutyCycle"/>.
    /// Sleeps in proportion to how long the batch just took, and throws if the job has
    /// been cancelled - so this doubles as the natural cancellation point of a work loop.
    /// </summary>
    Task Pace(CancellationToken cancellationToken);
}
