using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.Maps;

namespace WC3ChampionsStatisticService.Tests.Maps;

/// <summary>
/// One run of <see cref="TemporaryMapExpirySweep"/> as a whole: runs queue behind each other, a cancellation propagates,
/// the clock must be UTC, the summary line carries every counter and the constants match the spec. The two passes have
/// their own fixtures (<see cref="TemporaryMapExpirySweepExpiryPassTests"/>,
/// <see cref="TemporaryMapExpirySweepReconciliationPassTests"/>); shared scripting lives in
/// <see cref="TemporaryMapExpirySweepTestBase"/>.
/// </summary>
[TestFixture]
public class TemporaryMapExpirySweepTests : TemporaryMapExpirySweepTestBase
{
    [Test]
    public async Task ConcurrentRuns_ExecuteOneAfterTheOther()
    {
        // The daily run and an admin-triggered run queue behind each other rather than double-deleting; neither
        // is skipped. The first run is held inside its expiry listing while the second is started.
        var firstInListing = NewSignal();
        var secondInListing = NewSignal();
        var release = NewSignal();
        var arrivals = 0;
        var handler = EmptyReconciliation()
            .On(IsExpired, _ =>
            {
                if (Interlocked.Increment(ref arrivals) == 1)
                {
                    firstInListing.TrySetResult();
                    Assert.That(release.Task.Wait(HangGuard), Is.True, "the test never released the first run");
                }
                else
                {
                    secondInListing.TrySetResult();
                }

                return ScriptedHttpHandler.Json(HttpStatusCode.OK, Items());
            });
        var sweep = CreateSweep(handler);

        var first = Task.Run(() => sweep.RunOnceAsync(Now, CancellationToken.None));
        await firstInListing.Task.WaitAsync(HangGuard);
        var second = Task.Run(() => sweep.RunOnceAsync(Now, CancellationToken.None));
        var secondRanConcurrently = await Task.WhenAny(secondInListing.Task, Task.Delay(300)) == secondInListing.Task;
        release.TrySetResult();
        await Task.WhenAll(first, second).WaitAsync(HangGuard);

        Assert.That(secondRanConcurrently, Is.False, "the second run must wait for the first");
        Assert.That(handler.Requests.Select(SweepRoute), Is.EqualTo(new[] { "expired", "files", "expired", "files" }));
        Assert.That(first.Result.Failed, Is.Zero);
        Assert.That(second.Result.Failed, Is.Zero);
    }

    [Test]
    public async Task ACancelledRun_PropagatesTheCancellation_AndLeavesTheSweepFreeForTheNextRun()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = EmptyReconciliation()
            .On(IsExpired, _ =>
            {
                cancellation.Cancel();
                return ScriptedHttpHandler.Json(HttpStatusCode.OK, Items((1, FileA)));
            })
            .On(IsByPathProbe, ClaimsOf((1, FileA)))
            .On(HttpMethod.Delete, UsFile, HttpStatusCode.NoContent, "")
            .On(HttpMethod.Post, FileDeleted, HttpStatusCode.OK, "{\"map\":{\"id\":1}}");
        var sweep = CreateSweep(handler);

        Assert.CatchAsync<OperationCanceledException>(() => sweep.RunOnceAsync(Now, cancellation.Token));
        Assert.That(handler.CountRequests(HttpMethod.Delete, UsFile), Is.Zero, "nothing after the cancellation");

        var report = await sweep.RunOnceAsync(Now, CancellationToken.None).WaitAsync(HangGuard);
        Assert.That(report.Deleted, Is.EqualTo(1));
    }

    [Test]
    public void RunOnceAsync_RequiresAUtcClock()
    {
        // A local or unspecified DateTime would shift the TTL boundary by the server's zone.
        var sweep = CreateSweep(EmptyReconciliation());

        Assert.ThrowsAsync<ArgumentException>(() => sweep.RunOnceAsync(new DateTime(2026, 9, 14, 3, 0, 0, DateTimeKind.Unspecified), CancellationToken.None));
        Assert.ThrowsAsync<ArgumentException>(() => sweep.RunOnceAsync(new DateTime(2026, 9, 14, 3, 0, 0, DateTimeKind.Local), CancellationToken.None));
    }

    [Test]
    public async Task TheSummaryLine_CarriesEveryCounter()
    {
        using var logs = new LogCapture();
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items((1, FileA)))
            .On(HttpMethod.Get, Listing, HttpStatusCode.OK, Files(null, FileB))
            .On(r => IsByPathFor(r, FileA), Respond(HttpStatusCode.OK, Claim(1, FileA)))
            .On(HttpMethod.Get, ByPath, HttpStatusCode.NotFound)
            .On(HttpMethod.Delete, UsFile, HttpStatusCode.NoContent, "")
            .On(HttpMethod.Post, FileDeleted, HttpStatusCode.OK, "{\"map\":{\"id\":1}}");

        await CreateSweep(handler, logs.CreateLogger<TemporaryMapExpirySweep>()).RunOnceAsync(Now, CancellationToken.None);

        var summary = logs.Lines().Single(l => l.Contains("Temporary map sweep finished"));
        Assert.That(summary, Does.StartWith("Information"));
        Assert.That(summary, Does.Contain("Scanned=1").And.Contain("Deleted=1").And.Contain("ReclaimedOrphans=1")
            .And.Contain("Deferred=0").And.Contain("Failed=0").And.Contain("PurgedSpoolFiles=0"));
        Assert.That(logs.Lines().Single(l => l.Contains("ORPHAN_RECLAIMED")), Does.Contain(FileB));
    }

    [Test]
    public void SweepIntervalAndTtlMatchTheSpec()
    {
        Assert.That(TemporaryMapLimits.TtlDays, Is.EqualTo(30));
        Assert.That(TemporaryMapLimits.SweepIntervalHours, Is.EqualTo(24));
        Assert.That(TemporaryMapLimits.SweepBatchSize, Is.EqualTo(200));
        Assert.That(TemporaryMapLimits.OrphanMinAgeHours, Is.EqualTo(24));
        Assert.That(TemporaryMapLimits.StaleSpoolFileAgeHours, Is.EqualTo(24));
    }
}
