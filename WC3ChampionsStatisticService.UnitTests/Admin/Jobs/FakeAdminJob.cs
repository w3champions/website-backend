using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using W3C.Contracts.Admin.Permission;
using W3ChampionsStatisticService.Admin.Jobs;

namespace WC3ChampionsStatisticService.Tests.Admin.Jobs;

/// <summary>A job whose body the test supplies.</summary>
public class FakeAdminJob(string key, Func<IAdminJobContext, CancellationToken, Task> body) : IAdminJob
{
    public string Key { get; } = key;
    public string Name { get; set; } = key;
    public string Description { get; set; } = "fake";
    public EPermission RequiredPermission { get; set; } = EPermission.Jobs;
    public bool RequiresConfirmation { get; set; }

    /// <summary>Flat out by default so tests are not paced; overridden where that is the point.</summary>
    public double FallbackDutyCycle { get; set; } = 1.0;

    /// <summary>The checkpoint the job was handed, captured so tests can assert on resume.</summary>
    public BsonDocument ObservedCheckpoint { get; private set; }
    public long ObservedItemsProcessed { get; private set; }

    public Task RunAsync(IAdminJobContext context, CancellationToken cancellationToken)
    {
        ObservedCheckpoint = context.Checkpoint;
        ObservedItemsProcessed = context.ItemsProcessed;
        return body(context, cancellationToken);
    }
}

/// <summary>
/// Pressure readings the test dictates. Counters are cumulative, as the real probe's
/// are, so a test raises them to simulate the server complaining.
/// </summary>
public class FakePressureProbe : IPressureProbe
{
    public bool DatabaseAvailable { get; set; } = true;
    public long DirtyTriggerReached { get; set; }
    public long ApplicationThreadEvictions { get; set; }
    public double DirtyCacheFraction { get; set; }
    public double WriteTicketUtilisation { get; set; }
    public long QueuedWriters { get; set; }
    public int Samples { get; private set; }

    public Task<PressureSample> Sample(CancellationToken cancellationToken)
    {
        Samples++;
        return Task.FromResult(new PressureSample(
            TakenAt: DateTimeOffset.UtcNow,
            ProcessCpuTime: TimeSpan.Zero,
            ProcessorCount: 8,
            DatabaseAvailable: DatabaseAvailable,
            DirtyTriggerReached: DirtyTriggerReached,
            ApplicationThreadEvictions: ApplicationThreadEvictions,
            DirtyCacheFraction: DirtyCacheFraction,
            WriteTicketUtilisation: WriteTicketUtilisation,
            QueuedWriters: QueuedWriters));
    }
}

/// <summary>
/// In-memory <see cref="IAdminJobRepository"/> for runner tests. Claim semantics are
/// covered against real Mongo in AdminJobRepositoryTests; this only needs to record
/// what the runner asked for.
/// </summary>
public class FakeAdminJobRepository : IAdminJobRepository
{
    private readonly Dictionary<string, AdminJob> _jobs = [];

    public List<(string Key, AdminJobStatus Status, string Error)> Finishes { get; } = [];
    public int ProgressWrites { get; private set; }

    public AdminJob Seed(string key, AdminJob job)
    {
        job.Id = key;
        _jobs[key] = job;
        return job;
    }

    public Task<List<AdminJob>> LoadAll() => Task.FromResult(_jobs.Values.ToList());

    public Task<AdminJob> Load(string key) => Task.FromResult(_jobs.GetValueOrDefault(key));

    public Task<AdminJob> TryClaim(string key, string battleTag, bool force, bool reset)
    {
        var job = _jobs.GetValueOrDefault(key) ?? Seed(key, new AdminJob());
        if (job.Status == AdminJobStatus.Running)
        {
            return Task.FromResult<AdminJob>(null);
        }

        job.Status = AdminJobStatus.Running;
        job.TriggeredBy = battleTag;
        job.StartedAt = DateTimeOffset.UtcNow;
        job.RunCount++;
        if (reset)
        {
            job.Checkpoint = null;
            job.ItemsProcessed = 0;
        }

        return Task.FromResult(job);
    }

    public Task ReportProgress(string key, AdminJobProgress progress, BsonDocument checkpoint, long itemsProcessed)
    {
        ProgressWrites++;
        var job = _jobs[key];
        job.Progress = progress;
        job.ItemsProcessed = itemsProcessed;
        if (checkpoint != null)
        {
            job.Checkpoint = checkpoint;
        }

        return Task.CompletedTask;
    }

    public Task Finish(string key, AdminJobStatus status, string error)
    {
        Finishes.Add((key, status, error));
        var job = _jobs[key];
        job.Status = status;
        job.Error = error;
        job.FinishedAt = DateTimeOffset.UtcNow;
        return Task.CompletedTask;
    }

    public Task<List<string>> MarkRunningAsInterrupted()
    {
        var keys = _jobs.Values.Where(j => j.Status == AdminJobStatus.Running).Select(j => j.Id).ToList();
        foreach (var key in keys)
        {
            _jobs[key].Status = AdminJobStatus.Interrupted;
        }

        return Task.FromResult(keys);
    }
}
