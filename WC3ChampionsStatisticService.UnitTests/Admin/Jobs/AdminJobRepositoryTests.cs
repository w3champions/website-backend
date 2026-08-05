using System.Threading.Tasks;
using MongoDB.Bson;
using NUnit.Framework;
using W3ChampionsStatisticService.Admin.Jobs;

namespace WC3ChampionsStatisticService.Tests.Admin.Jobs;

[TestFixture]
public class AdminJobRepositoryTests : IntegrationTestBase
{
    private AdminJobRepository _repository;

    [SetUp]
    public void SetupRepository()
    {
        _repository = new AdminJobRepository(MongoClient);
    }

    private Task<AdminJob> Claim(bool force = false, bool reset = false) =>
        _repository.TryClaim("job", "peon#1", force, reset);

    private async Task<AdminJob> FinishAs(AdminJobStatus status, BsonDocument checkpoint = null, long items = 0)
    {
        if (checkpoint != null || items > 0)
        {
            await _repository.ReportProgress("job", new AdminJobProgress { Current = items }, checkpoint, items);
        }

        await _repository.Finish("job", status, status == AdminJobStatus.Failed ? "boom" : null);
        return await _repository.Load("job");
    }

    [Test]
    public async Task FirstClaimCreatesTheDocument()
    {
        var claimed = await Claim();

        Assert.That(claimed, Is.Not.Null);
        Assert.That(claimed.Status, Is.EqualTo(AdminJobStatus.Running));
        Assert.That(claimed.TriggeredBy, Is.EqualTo("peon#1"));
        Assert.That(claimed.RunCount, Is.EqualTo(1));
        Assert.That(claimed.StartedAt, Is.Not.Null);
    }

    [Test]
    public async Task ARunningJobCannotBeClaimedAgain()
    {
        await Claim();

        Assert.That(await Claim(), Is.Null);
        Assert.That(await Claim(force: true), Is.Null, "force is for re-running a finished job, not stealing a running one");
    }

    [Test]
    public async Task ACompletedJobNeedsForceToRunAgain()
    {
        await Claim();
        await FinishAs(AdminJobStatus.Completed);

        Assert.That(await Claim(), Is.Null);

        var forced = await Claim(force: true);
        Assert.That(forced, Is.Not.Null);
        Assert.That(forced.RunCount, Is.EqualTo(2));
    }

    [Test]
    public async Task AFailedJobCanBeRunAgainWithoutForce()
    {
        await Claim();
        await FinishAs(AdminJobStatus.Failed);

        var again = await Claim();

        Assert.That(again, Is.Not.Null);
        Assert.That(again.Error, Is.Null, "the previous run's error should not linger on a new run");
    }

    [Test]
    public async Task CancelledJobResumesFromItsCheckpointAndKeepsCounting()
    {
        await Claim();
        await FinishAs(AdminJobStatus.Cancelled, new BsonDocument("day", 12), items: 900);

        var resumed = await Claim();

        Assert.That(resumed.Checkpoint, Is.EqualTo(new BsonDocument("day", 12)));
        Assert.That(resumed.ItemsProcessed, Is.EqualTo(900));
    }

    [Test]
    public async Task ResetDiscardsTheCheckpointAndCounters()
    {
        await Claim();
        await FinishAs(AdminJobStatus.Cancelled, new BsonDocument("day", 12), items: 900);

        var restarted = await Claim(reset: true);

        Assert.That(restarted.Checkpoint, Is.Null);
        Assert.That(restarted.ItemsProcessed, Is.Zero);
        Assert.That(restarted.Progress.Current, Is.Zero);
    }

    [Test]
    public async Task ForcingACompletedJobStartsOverEvenThoughACheckpointRemains()
    {
        await Claim();
        await FinishAs(AdminJobStatus.Completed, new BsonDocument("day", 30), items: 5000);

        var forced = await Claim(force: true);

        Assert.That(forced.Checkpoint, Is.Null, "force on a completed job means run the whole thing again");
        Assert.That(forced.ItemsProcessed, Is.Zero);
    }

    [Test]
    public async Task DurationCountsWorkingTimeOnlyAcrossAPauseAndResume()
    {
        await Claim();
        await Task.Delay(60);
        await FinishAs(AdminJobStatus.Cancelled, new BsonDocument("day", 1), items: 10);
        var firstLeg = (await _repository.Load("job")).DurationMs;

        // Stand in for a job left paused: the gap before the resume must not be counted.
        await Task.Delay(250);

        await Claim();
        var finished = await FinishAs(AdminJobStatus.Completed);

        Assert.That(firstLeg, Is.GreaterThan(0));
        Assert.That(finished.DurationMs, Is.GreaterThanOrEqualTo(firstLeg));
        Assert.That(finished.DurationMs, Is.LessThan(firstLeg + 250),
            "the idle time between the two legs leaked into the reported duration");
    }

    [Test]
    public async Task ReportProgressLeavesTheCheckpointAloneWhenNoneIsGiven()
    {
        await Claim();
        await _repository.ReportProgress("job", new AdminJobProgress { Current = 1 }, new BsonDocument("day", 3), 1);
        await _repository.ReportProgress("job", new AdminJobProgress { Current = 2 }, null, 2);

        var state = await _repository.Load("job");

        Assert.That(state.Checkpoint, Is.EqualTo(new BsonDocument("day", 3)));
        Assert.That(state.ItemsProcessed, Is.EqualTo(2));
    }

    [Test]
    public async Task InterruptedSweepOnlyTouchesRunningJobs()
    {
        await _repository.TryClaim("running", "peon#1", false, false);
        await _repository.TryClaim("finished", "peon#1", false, false);
        await _repository.Finish("finished", AdminJobStatus.Completed, null);

        var swept = await _repository.MarkRunningAsInterrupted();

        Assert.That(swept, Is.EquivalentTo(new[] { "running" }));
        Assert.That((await _repository.Load("running")).Status, Is.EqualTo(AdminJobStatus.Interrupted));
        Assert.That((await _repository.Load("finished")).Status, Is.EqualTo(AdminJobStatus.Completed));
    }

    [Test]
    public async Task AnInterruptedJobResumesRatherThanStartingOver()
    {
        await Claim();
        await _repository.ReportProgress("job", new AdminJobProgress { Current = 5 }, new BsonDocument("day", 5), 500);
        await _repository.MarkRunningAsInterrupted();

        var resumed = await Claim();

        Assert.That(resumed.Status, Is.EqualTo(AdminJobStatus.Running));
        Assert.That(resumed.Checkpoint, Is.EqualTo(new BsonDocument("day", 5)));
        Assert.That(resumed.ItemsProcessed, Is.EqualTo(500));
    }
}
