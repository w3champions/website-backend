using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using NUnit.Framework;
using W3ChampionsStatisticService.Admin.Jobs;

namespace WC3ChampionsStatisticService.Tests.Admin.Jobs;

[TestFixture]
public class AdminJobRunnerTests
{
    private FakeAdminJobRepository _repository;

    [SetUp]
    public void Setup()
    {
        _repository = new FakeAdminJobRepository();
    }

    private AdminJobRunner CreateRunner(params IAdminJob[] jobs)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAdminJobRepository>(_repository);
        foreach (var job in jobs)
        {
            services.AddSingleton(job);
        }

        return new AdminJobRunner(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());
    }

    [Test]
    public async Task RunToCompletion_RecordsCompleted()
    {
        var job = new FakeAdminJob("done", (_, _) => Task.CompletedTask);
        var runner = CreateRunner(job);

        var result = await runner.TryStart("done", "peon#1", force: false, reset: false);
        await runner.WhenFinished("done");

        Assert.That(result, Is.EqualTo(AdminJobStartResult.Started));
        Assert.That(_repository.Finishes, Is.EquivalentTo(new[] { ("done", AdminJobStatus.Completed, (string)null) }));
    }

    [Test]
    public async Task UnknownKey_IsNotFound()
    {
        var runner = CreateRunner();

        var result = await runner.TryStart("nope", "peon#1", force: false, reset: false);

        Assert.That(result, Is.EqualTo(AdminJobStartResult.NotFound));
        Assert.That(_repository.Finishes, Is.Empty);
    }

    [Test]
    public async Task ThrowingJob_IsRecordedAsFailedWithItsMessage()
    {
        var job = new FakeAdminJob("boom", (_, _) => throw new InvalidOperationException("it broke"));
        var runner = CreateRunner(job);

        await runner.TryStart("boom", "peon#1", force: false, reset: false);
        await runner.WhenFinished("boom");

        Assert.That(_repository.Finishes, Is.EquivalentTo(new[] { ("boom", AdminJobStatus.Failed, "it broke") }));
    }

    [Test]
    public async Task AlreadyRunning_IsRefusedRatherThanRunTwice()
    {
        var release = new TaskCompletionSource();
        var job = new FakeAdminJob("slow", (_, _) => release.Task);
        var runner = CreateRunner(job);

        var first = await runner.TryStart("slow", "peon#1", force: false, reset: false);
        var second = await runner.TryStart("slow", "peon#2", force: false, reset: false);

        Assert.That(first, Is.EqualTo(AdminJobStartResult.Started));
        Assert.That(second, Is.EqualTo(AdminJobStartResult.NotStartable));

        release.SetResult();
        await runner.WhenFinished("slow");
    }

    [Test]
    public async Task Cancel_StopsTheJobAndRecordsCancelled()
    {
        var started = new TaskCompletionSource();
        var job = new FakeAdminJob("cancellable", async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
        });
        var runner = CreateRunner(job);

        await runner.TryStart("cancellable", "peon#1", force: false, reset: false);
        await started.Task;

        Assert.That(runner.Cancel("cancellable"), Is.True);
        await runner.WhenFinished("cancellable");

        Assert.That(_repository.Finishes, Is.EquivalentTo(new[] { ("cancellable", AdminJobStatus.Cancelled, (string)null) }));
    }

    [Test]
    public void Cancel_WhenNotRunning_ReportsNothingToCancel()
    {
        var runner = CreateRunner(new FakeAdminJob("idle", (_, _) => Task.CompletedTask));

        Assert.That(runner.Cancel("idle"), Is.False);
    }

    [Test]
    public async Task CheckpointIsFlushedEvenWhenTheJobIsCancelled()
    {
        var started = new TaskCompletionSource();
        var job = new FakeAdminJob("checkpointing", async (context, token) =>
        {
            // Reported inside the throttle window, so only the runner's final flush can
            // get it to the database - which is exactly what a resume depends on.
            await context.Report(1, 10, "day 1", new BsonDocument("day", 1));
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
        });
        var runner = CreateRunner(job);

        await runner.TryStart("checkpointing", "peon#1", force: false, reset: false);
        await started.Task;
        runner.Cancel("checkpointing");
        await runner.WhenFinished("checkpointing");

        var state = await _repository.Load("checkpointing");
        Assert.That(state.Checkpoint, Is.EqualTo(new BsonDocument("day", 1)));
        Assert.That(state.Progress.Current, Is.EqualTo(1));
        Assert.That(state.Progress.Message, Is.EqualTo("day 1"));
    }

    [Test]
    public async Task ResumedJobSeesItsPreviousCheckpointAndCount()
    {
        var job = new FakeAdminJob("resumable", (_, _) => Task.CompletedTask);
        _repository.Seed("resumable", new AdminJob
        {
            Status = AdminJobStatus.Interrupted,
            Checkpoint = new BsonDocument("day", 7),
            ItemsProcessed = 4200,
        });
        var runner = CreateRunner(job);

        await runner.TryStart("resumable", "peon#1", force: false, reset: false);
        await runner.WhenFinished("resumable");

        Assert.That(job.ObservedCheckpoint, Is.EqualTo(new BsonDocument("day", 7)));
        Assert.That(job.ObservedItemsProcessed, Is.EqualTo(4200));
    }

    [Test]
    public async Task ShutdownCancelsRunningJobsAndRecordsThemAsInterrupted()
    {
        var started = new TaskCompletionSource();
        var job = new FakeAdminJob("long", async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
        });
        var runner = CreateRunner(job);

        await runner.TryStart("long", "peon#1", force: false, reset: false);
        await started.Task;
        await runner.StopAsync(CancellationToken.None);

        // Interrupted rather than Cancelled: nobody chose to stop it.
        Assert.That(_repository.Finishes, Is.EquivalentTo(new[] { ("long", AdminJobStatus.Interrupted, (string)null) }));
    }

    [Test]
    public async Task StartupSweepMarksJobsLeftRunningByADeadProcess()
    {
        _repository.Seed("stale", new AdminJob { Status = AdminJobStatus.Running });
        var runner = CreateRunner();

        await runner.StartAsync(CancellationToken.None);

        Assert.That((await _repository.Load("stale")).Status, Is.EqualTo(AdminJobStatus.Interrupted));
    }

    [Test]
    public async Task PacingHoldsAJobToItsDutyCycle()
    {
        var job = new FakeAdminJob("paced", async (context, token) =>
        {
            for (var i = 0; i < 3; i++)
            {
                await Task.Delay(50, token);
                await context.Pace(token);
            }
        })
        { MaxDutyCycle = 0.25 };
        var runner = CreateRunner(job);

        var start = DateTimeOffset.UtcNow;
        await runner.TryStart("paced", "peon#1", force: false, reset: false);
        await runner.WhenFinished("paced");
        var elapsed = DateTimeOffset.UtcNow - start;

        // 150ms of work at a 25% duty cycle owes roughly 450ms of pause. Asserting well
        // under that only proves it paced at all, without depending on timer accuracy.
        Assert.That(elapsed, Is.GreaterThan(TimeSpan.FromMilliseconds(300)));
        Assert.That(_repository.Finishes[0].Status, Is.EqualTo(AdminJobStatus.Completed));
    }

    [Test]
    public async Task PacingObservesCancellation()
    {
        var started = new TaskCompletionSource();
        var job = new FakeAdminJob("paced-cancel", async (context, token) =>
        {
            started.SetResult();
            while (true)
            {
                await Task.Delay(20, token);
                await context.Pace(token);
            }
        })
        { MaxDutyCycle = 0.01 };
        var runner = CreateRunner(job);

        await runner.TryStart("paced-cancel", "peon#1", force: false, reset: false);
        await started.Task;
        runner.Cancel("paced-cancel");
        await runner.WhenFinished("paced-cancel");

        // A job asleep inside Pace must still stop promptly rather than serving out its
        // pause, or cancel would take up to MaxPause to take effect.
        Assert.That(_repository.Finishes[0].Status, Is.EqualTo(AdminJobStatus.Cancelled));
    }

    [Test]
    public async Task ProgressWritesAreThrottled()
    {
        var job = new FakeAdminJob("chatty", async (context, _) =>
        {
            for (var i = 0; i < 500; i++)
            {
                await context.Report(i, 500);
            }
        });
        var runner = CreateRunner(job);

        await runner.TryStart("chatty", "peon#1", force: false, reset: false);
        await runner.WhenFinished("chatty");

        // 500 reports must not be 500 writes. Normally this collapses to the runner's
        // single closing flush; the bound is loose so a slow machine crossing a 2s
        // window does not fail the test.
        Assert.That(_repository.ProgressWrites, Is.LessThan(5));
        Assert.That((await _repository.Load("chatty")).Progress.Current, Is.EqualTo(499));
    }
}
