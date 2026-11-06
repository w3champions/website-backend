using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using W3ChampionsStatisticService.Admin.Jobs;

namespace WC3ChampionsStatisticService.Tests.Admin.Jobs;

/// <summary>
/// A context for testing a job body in isolation from the runner: records every report and
/// item count, paces without pausing, and hands the job the checkpoint the test supplies.
/// </summary>
public class FakeAdminJobContext(BsonDocument checkpoint = null) : IAdminJobContext
{
    public BsonDocument Checkpoint { get; } = checkpoint;

    public long ItemsProcessed { get; private set; }

    public List<(long Current, long Total, string Message)> Reports { get; } = [];

    /// <summary>The most recent checkpoint the job reported.</summary>
    public BsonDocument LastCheckpoint { get; private set; }

    public int Paces { get; private set; }

    public Task Report(long current, long total, string message = null, BsonDocument checkpoint = null)
    {
        Reports.Add((current, total, message));
        LastCheckpoint = checkpoint ?? LastCheckpoint;
        return Task.CompletedTask;
    }

    public void AddItems(long count) => ItemsProcessed += count;

    public Task Pace(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Paces++;
        return Task.CompletedTask;
    }
}
