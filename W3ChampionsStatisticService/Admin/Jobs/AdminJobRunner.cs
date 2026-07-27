using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Admin.Jobs;

/// <summary>
/// Starts, tracks and cancels admin jobs in-process.
/// <para>
/// <b>This assumes the service runs as a single instance.</b> There is no queue and no
/// polling loop: the API calls <see cref="TryStart"/> directly and the job runs here.
/// The assumption is only actually baked into <see cref="StartAsync"/> - see the
/// comment there before scaling out.
/// </para>
/// </summary>
[Trace]
public class AdminJobRunner(IServiceScopeFactory scopeFactory) : IHostedService
{
    private readonly ConcurrentDictionary<string, RunningJob> _running = new();

    /// <summary>Cancels every running job when the host shuts down.</summary>
    private readonly CancellationTokenSource _shutdown = new();

    private sealed record RunningJob(CancellationTokenSource Cancellation)
    {
        /// <summary>Completes when the job has finished and written its final state.</summary>
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public async Task<AdminJobStartResult> TryStart(string key, string battleTag, bool force, bool reset)
    {
        // A scope of the runner's own, not the caller's: the request scope is disposed
        // when the HTTP response completes, which would tear scoped services out from
        // under a job that outlives the request.
        using var scope = scopeFactory.CreateScope();

        var job = FindJob(scope.ServiceProvider, key);
        if (job == null)
        {
            return AdminJobStartResult.NotFound;
        }

        var repository = scope.ServiceProvider.GetRequiredService<IAdminJobRepository>();
        var claimed = await repository.TryClaim(key, battleTag, force, reset);
        if (claimed == null)
        {
            return AdminJobStartResult.NotStartable;
        }

        var running = new RunningJob(CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token));
        if (!_running.TryAdd(key, running))
        {
            // The database said the job was startable but we already track it running.
            // Only reachable if state drifted; refuse rather than run it twice.
            running.Cancellation.Dispose();
            Log.Warning("Admin job {JobKey} was claimed but is already tracked as running", key);
            return AdminJobStartResult.NotStartable;
        }

        // Deliberately not awaited - the HTTP request returns immediately and the job
        // continues in the background. Exceptions are handled inside Execute.
        _ = Execute(key, claimed, running);

        return AdminJobStartResult.Started;
    }

    public bool Cancel(string key)
    {
        if (!_running.TryGetValue(key, out var running))
        {
            return false;
        }

        Log.Information("Admin job {JobKey} cancellation requested", key);
        running.Cancellation.Cancel();
        return true;
    }

    public bool IsRunning(string key) => _running.ContainsKey(key);

    /// <summary>
    /// Completes once the job has finished and written its final state, or immediately
    /// if it is not running.
    /// </summary>
    public Task WhenFinished(string key) =>
        _running.TryGetValue(key, out var running) ? running.Finished.Task : Task.CompletedTask;

    private async Task Execute(string key, AdminJob claimed, RunningJob running)
    {
        // A fresh scope for the job itself. TryStart's scope is gone by the time this
        // runs, and the job may hold its dependencies for hours.
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAdminJobRepository>();
        var context = new AdminJobContext(key, repository, claimed);

        var status = AdminJobStatus.Completed;
        string error = null;

        try
        {
            var job = FindJob(scope.ServiceProvider, key);
            Log.Information("Admin job {JobKey} starting (run {RunCount})", key, claimed.RunCount);

            await job.RunAsync(context, running.Cancellation.Token);

            Log.Information("Admin job {JobKey} completed, {ItemsProcessed} items", key, context.ItemsProcessed);
        }
        catch (OperationCanceledException) when (running.Cancellation.IsCancellationRequested)
        {
            // Shutdown counts as Interrupted rather than Cancelled: nobody chose to stop
            // it, and the distinction is what an admin looking at the row needs. Both
            // resume from the checkpoint identically.
            status = _shutdown.IsCancellationRequested ? AdminJobStatus.Interrupted : AdminJobStatus.Cancelled;
            Log.Information("Admin job {JobKey} stopped as {Status} after {ItemsProcessed} items",
                key, status, context.ItemsProcessed);
        }
        catch (Exception ex)
        {
            status = AdminJobStatus.Failed;
            error = ex.Message;
            Log.Error(ex, "Admin job {JobKey} failed", key);
        }
        finally
        {
            try
            {
                // Persist whatever the job last reported before recording the outcome,
                // so a resumed run picks up from its real position.
                await context.Flush();
                await repository.Finish(key, status, error);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Admin job {JobKey} finished as {Status} but its state could not be saved", key, status);
            }

            // Stop tracking only once the final state is written. Dropping it earlier
            // would leave a window where the job reads as not running while the database
            // still says Running, and a new start could be accepted against it.
            _running.TryRemove(key, out _);
            running.Cancellation.Dispose();
            running.Finished.TrySetResult();
        }
    }

    private static IAdminJob FindJob(IServiceProvider services, string key) =>
        services.GetServices<IAdminJob>().FirstOrDefault(j => j.Key == key);

    public static List<IAdminJob> AllJobs(IServiceProvider services) =>
        services.GetServices<IAdminJob>().OrderBy(j => j.Name).ToList();

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAdminJobRepository>();

        // Anything still marked Running belongs to a process that no longer exists, so
        // it is safe to mark it Interrupted and let an admin resume it. This is the one
        // place the single-instance assumption is load-bearing: with a second replica,
        // a booting replica would mark a job legitimately running elsewhere as dead.
        // Adding replicas therefore means adding an owner id and a heartbeat here.
        try
        {
            var interrupted = await repository.MarkRunningAsInterrupted();
            if (interrupted.Count > 0)
            {
                Log.Warning("Marked admin jobs as interrupted after restart: {JobKeys}", string.Join(", ", interrupted));
            }
        }
        catch (Exception ex)
        {
            // Never block startup for this - the worst case is a stale Running row that
            // an admin can reset.
            Log.Error(ex, "Could not sweep interrupted admin jobs at startup");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _shutdown.CancelAsync();

        // Give running jobs the chance to write their checkpoint on the way out, so a
        // routine deploy resumes from where it stopped instead of redoing a batch.
        // cancellationToken is the host's shutdown timeout (5s by default) - once it
        // fires, the process is going away regardless and the startup sweep will pick
        // up whatever was left marked Running.
        var pending = _running.Values.Select(r => r.Finished.Task).ToArray();
        if (pending.Length == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(pending).WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Log.Warning("Shutdown timed out with {Count} admin job(s) still stopping", pending.Length);
        }
    }
}

public enum AdminJobStartResult
{
    Started,
    NotFound,

    /// <summary>Already running, or completed and <c>force</c> was not passed.</summary>
    NotStartable,
}
