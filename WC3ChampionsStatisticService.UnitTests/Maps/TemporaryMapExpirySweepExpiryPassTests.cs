using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.Maps;

namespace WC3ChampionsStatisticService.Tests.Maps;

/// <summary>
/// The expiry pass of <see cref="TemporaryMapExpirySweep"/>: what the sweep asks matchmaking for, the by-path cross-check
/// before every delete, the batch loop, per-item isolation and the fileKey lock. Shared scripting lives in
/// <see cref="TemporaryMapExpirySweepTestBase"/>.
/// </summary>
[TestFixture]
public class TemporaryMapExpirySweepExpiryPassTests : TemporaryMapExpirySweepTestBase
{
    [Test]
    public async Task ExpiryPass_AsksForMapsOlderThanTheTtl()
    {
        var handler = EmptyReconciliation().On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items());

        await CreateSweep(handler).RunOnceAsync(Now, CancellationToken.None);

        var query = handler.LastRequest(HttpMethod.Get, Expired).RequestUri!.Query;
        var expectedBefore = new DateTimeOffset(Now.AddDays(-TemporaryMapLimits.TtlDays)).ToUnixTimeMilliseconds();
        Assert.That(query, Does.Contain($"before={expectedBefore}"));
        Assert.That(query, Does.Contain($"limit={TemporaryMapLimits.SweepBatchSize}"));
    }

    [Test]
    public async Task ExpiryPass_DeletesTheFileThenMarksTheRecord()
    {
        var handler = EmptyReconciliation()
            .On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items((1, FileA), (2, FileB)))
            .On(IsByPathProbe, ClaimsOf((1, FileA), (2, FileB)))
            .On(HttpMethod.Delete, UsFile, HttpStatusCode.NoContent, "")
            .On(HttpMethod.Post, FileDeleted, HttpStatusCode.OK, "{\"map\":{\"id\":1}}");

        var report = await CreateSweep(handler).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(report.Deleted, Is.EqualTo(2));
        Assert.That(report.Failed, Is.Zero);
        Assert.That(handler.CountRequests(HttpMethod.Delete, UsFile), Is.EqualTo(2));
        Assert.That(handler.CountRequests(HttpMethod.Post, "/maps/temporary/1/file-deleted"), Is.EqualTo(1));
        Assert.That(handler.CountRequests(HttpMethod.Post, "/maps/temporary/2/file-deleted"), Is.EqualTo(1));
        Assert.That(handler.CountRequests(HttpMethod.Get, Expired), Is.EqualTo(1), "a partial batch is the last one");

        var probeIndex = handler.Requests.FindIndex(IsByPathProbe);
        var deleteIndex = handler.Requests.FindIndex(r => r.Method == HttpMethod.Delete);
        var markIndex = handler.Requests.FindIndex(r => r.RequestUri!.PathAndQuery.Contains(FileDeleted));
        Assert.That(probeIndex, Is.LessThan(deleteIndex), "the record is asked about first");
        Assert.That(deleteIndex, Is.LessThan(markIndex), "the bytes go first; the record follows");
    }

    private static IEnumerable<TestCaseData> ClaimsThatDoNotExpireTheItem()
    {
        yield return new TestCaseData(Respond(HttpStatusCode.OK, Claim(2, FileA))).SetName("ExpiryPass_RefusesAnItem_WhoseByPathClaimIsAnotherRecord");
        yield return new TestCaseData(Respond(HttpStatusCode.OK, Claim(1, FileB))).SetName("ExpiryPass_RefusesAnItem_WhoseByPathClaimNamesAnotherPath");
        yield return new TestCaseData(Respond(HttpStatusCode.OK, Claim(1, FileA, "deleted"))).SetName("ExpiryPass_RefusesAnItem_WhoseByPathClaimSaysDeleted");
        yield return new TestCaseData(Respond(HttpStatusCode.OK, Claim(1, FileA, "archived"))).SetName("ExpiryPass_RefusesAnItem_WhoseByPathClaimHasAnUnknownFileState");
        yield return new TestCaseData(Respond(HttpStatusCode.OK, Claim(1, FileA, lastHostedAt: Before))).SetName("ExpiryPass_RefusesAnItem_HostedExactlyAtTheTtlBoundary");
        yield return new TestCaseData(Respond(HttpStatusCode.OK, Claim(1, FileA, lastHostedAt: Before + 1))).SetName("ExpiryPass_RefusesAnItem_HostedSinceTheTtlBoundary");
        yield return new TestCaseData(Respond(HttpStatusCode.OK, Claim(1, FileA, lastHostedAt: null))).SetName("ExpiryPass_RefusesAnItem_WhoseByPathClaimHasNoLastHostedAt");
        yield return new TestCaseData(Respond(HttpStatusCode.NotFound)).SetName("ExpiryPass_RefusesAnItem_NoRecordClaims");
        yield return new TestCaseData(Respond(HttpStatusCode.NotFound, "{\"message\":\"Cannot GET /maps/temporary/by-path\"}")).SetName("ExpiryPass_RefusesAnItem_WhenTheByPathRouteIsMissing");
        yield return new TestCaseData(TransportFails()).SetName("ExpiryPass_RefusesAnItem_WhenTheByPathProbeFails");
    }

    [TestCaseSource(nameof(ClaimsThatDoNotExpireTheItem))]
    public async Task ExpiryPass_DeletesNothing_UnlessTheRecordItselfConfirmsTheExpiry(Func<HttpRequestMessage, HttpResponseMessage> byPath)
    {
        // The expired listing is one matchmaking answer; before acting on it destructively the sweep asks matchmaking
        // about the path itself and requires the very record, present, last hosted before the boundary it sent. A
        // listing that ignored `before` (or read it in the wrong unit) would otherwise delete every temporary map's
        // bytes in one run. Anything short of that confirmation is the item's failure: no delete, no mark, next run.
        using var logs = new LogCapture();
        var handler = EmptyReconciliation()
            .On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items((1, FileA), (2, FileB)))
            .On(r => IsByPathFor(r, FileA), byPath)
            .On(r => IsByPathFor(r, FileB), Respond(HttpStatusCode.OK, Claim(2, FileB)))
            .On(HttpMethod.Delete, UsFile, HttpStatusCode.NoContent, "")
            .On(HttpMethod.Post, FileDeleted, HttpStatusCode.OK, "{\"map\":{\"id\":2}}");

        var report = await CreateSweep(handler, logs.CreateLogger<TemporaryMapExpirySweep>()).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(report.Failed, Is.EqualTo(1));
        Assert.That(report.Deleted, Is.EqualTo(1), "the confirmed item is still expired");
        Assert.That(handler.CountRequests(HttpMethod.Delete, UsFile), Is.EqualTo(1));
        Assert.That(Uri.UnescapeDataString(handler.LastRequest(HttpMethod.Delete, UsFile).RequestUri!.Query), Does.Contain(FileB).And.Not.Contain(FileA));
        Assert.That(handler.CountRequests(HttpMethod.Post, "/maps/temporary/1/file-deleted"), Is.Zero, "a record whose expiry was not confirmed is never marked");
        Assert.That(handler.Requests.Count(r => IsByPathFor(r, FileA)), Is.EqualTo(1), "asked once per run");
        var warnings = logs.Lines().Where(l => l.StartsWith("Warning", StringComparison.Ordinal)).ToArray();
        Assert.That(warnings, Has.Length.EqualTo(1), "one warning per refused item");
        Assert.That(warnings[0], Does.Contain("MapId=1").And.Contain($"FileKey=\"{FileA}\""));
    }

    [Test]
    public async Task ExpiryPass_IsolatesAPerItemFailure_AndKeepsGoing()
    {
        var handler = EmptyReconciliation()
            .On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items((1, FileA), (2, FileB)))
            .On(IsByPathProbe, ClaimsOf((1, FileA), (2, FileB)))
            .On(r => r.Method == HttpMethod.Delete && r.RequestUri!.Query.Contains("a-11111111"),
                Respond(HttpStatusCode.InternalServerError, "{}"))
            .On(HttpMethod.Delete, UsFile, HttpStatusCode.NoContent, "")
            .On(HttpMethod.Post, FileDeleted, HttpStatusCode.OK, "{\"map\":{\"id\":2}}");

        var report = await CreateSweep(handler).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(report.Failed, Is.EqualTo(1));
        Assert.That(report.Deleted, Is.EqualTo(1));
        Assert.That(handler.CountRequests(HttpMethod.Post, "/maps/temporary/1/file-deleted"), Is.Zero,
            "a record whose file could not be deleted must NOT be marked deleted; the next run retries it");
        Assert.That(handler.CountRequests(HttpMethod.Post, "/maps/temporary/2/file-deleted"), Is.EqualTo(1));
    }

    [Test]
    public async Task ExpiryPass_AnHttpClientTimeoutOnOneItem_IsThatItemsFailure_NotTheRunsEnd()
    {
        // An HttpClient timeout surfaces as a TaskCanceledException although nobody cancelled the sweep: it is an
        // upstream failure of that item, and the run goes on to the next one and to the reconciliation pass.
        var handler = EmptyReconciliation()
            .On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items((1, FileA), (2, FileB)))
            .On(IsByPathProbe, ClaimsOf((1, FileA), (2, FileB)))
            .On(r => r.Method == HttpMethod.Delete && r.RequestUri!.Query.Contains("a-11111111"), TimesOut())
            .On(HttpMethod.Delete, UsFile, HttpStatusCode.NoContent, "")
            .On(HttpMethod.Post, FileDeleted, HttpStatusCode.OK, "{\"map\":{\"id\":2}}");

        var report = await CreateSweep(handler).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(report.Failed, Is.EqualTo(1));
        Assert.That(report.Deleted, Is.EqualTo(1));
        Assert.That(handler.CountRequests(HttpMethod.Post, "/maps/temporary/2/file-deleted"), Is.EqualTo(1));
        Assert.That(handler.CountRequests(HttpMethod.Get, Listing), Is.EqualTo(1), "the reconciliation pass still ran");
    }

    [Test]
    public async Task ExpiryPass_AFailedMark_LeavesTheRecordForTheNextRun()
    {
        // The bytes are gone but the record still says present: the next run's delete is idempotent (204 either way)
        // and the mark is retried then. Nothing counts as deleted until the record says so.
        var handler = EmptyReconciliation()
            .On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items((1, FileA)))
            .On(IsByPathProbe, ClaimsOf((1, FileA)))
            .On(HttpMethod.Delete, UsFile, HttpStatusCode.NoContent, "")
            .On(HttpMethod.Post, FileDeleted, HttpStatusCode.InternalServerError, "{\"errors\":[{\"param\":\"id\",\"message\":\"boom\"}]}");

        var report = await CreateSweep(handler).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(report.Deleted, Is.Zero);
        Assert.That(report.Failed, Is.EqualTo(1));
    }

    [Test]
    public async Task ExpiryPass_FetchesTheNextBatch_WhileABatchWasFullAndMadeProgress()
    {
        // The listing has no cursor, so a full batch that made progress is followed by another listing; the
        // items deleted so far are no longer expired-and-present, so the next page starts after them.
        var handler = EmptyReconciliation()
            .On(IsExpired, Sequence(
                Items(Range(1, TemporaryMapLimits.SweepBatchSize)),
                Items(Range(TemporaryMapLimits.SweepBatchSize + 1, TemporaryMapLimits.SweepBatchSize)),
                Items(Range(2 * TemporaryMapLimits.SweepBatchSize + 1, 50))))
            .On(IsByPathProbe, ClaimsOf(Range(1, 2 * TemporaryMapLimits.SweepBatchSize + 50)))
            .On(HttpMethod.Delete, UsFile, HttpStatusCode.NoContent, "")
            .On(HttpMethod.Post, FileDeleted, HttpStatusCode.OK, "{\"map\":{\"id\":1}}");

        var report = await CreateSweep(handler).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(handler.CountRequests(HttpMethod.Get, Expired), Is.EqualTo(3));
        Assert.That(report.Deleted, Is.EqualTo(2 * TemporaryMapLimits.SweepBatchSize + 50));
        Assert.That(report.Failed, Is.Zero);
    }

    [Test]
    public async Task ExpiryPass_StopsAfterAFullBatchThatMadeNoProgress()
    {
        // Every delete fails: fetching the same batch again would only repeat the same failures in the same run.
        var batch = Items(Range(1, TemporaryMapLimits.SweepBatchSize));
        var handler = EmptyReconciliation()
            .On(IsExpired, Sequence(batch, batch, batch))
            .On(IsByPathProbe, ClaimsOf(Range(1, TemporaryMapLimits.SweepBatchSize)))
            .On(HttpMethod.Delete, UsFile, HttpStatusCode.InternalServerError, "{}");

        var report = await CreateSweep(handler).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(handler.CountRequests(HttpMethod.Get, Expired), Is.EqualTo(1));
        Assert.That(report.Failed, Is.EqualTo(TemporaryMapLimits.SweepBatchSize));
        Assert.That(report.Deleted, Is.Zero);
    }

    [Test]
    public async Task ExpiryPass_AttemptsEachItemOnceARun()
    {
        // One item of a full batch succeeds, the rest fail, and matchmaking answers the next listing with the same
        // full batch (the marked item still listed): nothing is attempted twice, so the batch made no progress and the
        // pass ends. Every failing item was tried exactly once; the next daily run tries them again.
        var batch = Items(Range(1, TemporaryMapLimits.SweepBatchSize));
        var handler = EmptyReconciliation()
            .On(IsExpired, Sequence(batch, batch, batch))
            .On(IsByPathProbe, ClaimsOf(Range(1, TemporaryMapLimits.SweepBatchSize)))
            .On(r => r.Method == HttpMethod.Delete && r.RequestUri!.Query.Contains("m-00000001"), Respond(HttpStatusCode.NoContent, ""))
            .On(HttpMethod.Delete, UsFile, HttpStatusCode.InternalServerError, "{}")
            .On(HttpMethod.Post, FileDeleted, HttpStatusCode.OK, "{\"map\":{\"id\":1}}");

        var report = await CreateSweep(handler).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(handler.CountRequests(HttpMethod.Get, Expired), Is.EqualTo(2), "full and progressing, so listed again; then no progress");
        Assert.That(handler.CountRequests(HttpMethod.Delete, UsFile), Is.EqualTo(TemporaryMapLimits.SweepBatchSize), "each item once");
        Assert.That(report.Deleted, Is.EqualTo(1));
        Assert.That(report.Failed, Is.EqualTo(TemporaryMapLimits.SweepBatchSize - 1));
    }

    [Test]
    public async Task ExpiryPass_AnItemWhosePathIsNotATemporaryMapFile_FailsWithoutADelete_AndIsLoggedAsInvalid()
    {
        // matchmaking drift: the sweep refuses the row itself, before the client's own guard could throw, and
        // the warning never renders the supplied path (it could forge a log line).
        using var logs = new LogCapture();
        var handler = EmptyReconciliation()
            .On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items((1, "maps\\\\evil\\r\\ninjected.w3x")))
            .On(HttpMethod.Delete, UsFile, HttpStatusCode.NoContent, "");

        var report = await CreateSweep(handler, logs.CreateLogger<TemporaryMapExpirySweep>()).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(report.Failed, Is.EqualTo(1));
        Assert.That(handler.CountRequests(HttpMethod.Delete, UsFile), Is.Zero);
        Assert.That(handler.CountRequests(HttpMethod.Post, FileDeleted), Is.Zero);
        var warning = logs.Lines().Single(l => l.StartsWith("Warning", StringComparison.Ordinal));
        Assert.That(warning, Does.Contain("FileKey=\"invalid\"").And.Contain("MapId=1"));
        Assert.That(warning, Does.Not.Contain("Exception"), "refused by the sweep's own check, not by a thrown client guard");
        Assert.That(logs.Lines(), Has.None.Contains("evil"));
    }

    [TestCase(0)]
    [TestCase(-7)]
    public async Task ExpiryPass_ARowWithoutAValidId_FailsWithoutADelete(int id)
    {
        // With the bytes deleted, a record that cannot be marked would stay present without them, every run.
        using var logs = new LogCapture();
        var handler = EmptyReconciliation()
            .On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items((id, FileA), (2, FileB)))
            .On(IsByPathProbe, ClaimsOf((2, FileB)))
            .On(HttpMethod.Delete, UsFile, HttpStatusCode.NoContent, "")
            .On(HttpMethod.Post, FileDeleted, HttpStatusCode.OK, Record(2, FileB, "deleted"));

        var report = await CreateSweep(handler, logs.CreateLogger<TemporaryMapExpirySweep>()).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(report.Failed, Is.EqualTo(1));
        Assert.That(report.Deleted, Is.EqualTo(1), "the valid row is still expired");
        Assert.That(handler.Requests.Count(r => IsByPathFor(r, FileA)), Is.Zero, "a row refused by shape is not even asked about");
        Assert.That(handler.CountRequests(HttpMethod.Delete, UsFile), Is.EqualTo(1));
        Assert.That(Uri.UnescapeDataString(handler.LastRequest(HttpMethod.Delete, UsFile).RequestUri!.Query), Does.Contain(FileB));
        Assert.That(handler.CountRequests(HttpMethod.Post, FileDeleted), Is.EqualTo(1));
        var warning = logs.Lines().Single(l => l.StartsWith("Warning", StringComparison.Ordinal));
        Assert.That(warning, Does.Contain($"MapId={id}").And.Contain($"FileKey=\"{FileA}\""));
    }

    [Test]
    public async Task ExpiryPass_DeletesAndMarksUnderTheFileKeyLock_AnUploadHolds()
    {
        // The upload service holds the fileKey from its store to its record write. Nothing of this item happens
        // until it lets go; then the item completes and the key is free again.
        var sweepWaiting = NewSignal();
        FileKeyLock = new TemporaryMapFileKeyLock { OnContended = _ => sweepWaiting.TrySetResult() };
        var handler = EmptyReconciliation()
            .On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items((1, FileA)))
            .On(IsByPathProbe, ClaimsOf((1, FileA)))
            .On(HttpMethod.Delete, UsFile, HttpStatusCode.NoContent, "")
            .On(HttpMethod.Post, FileDeleted, HttpStatusCode.OK, Record(1, FileA, "deleted"));
        var sweep = CreateSweep(handler);

        Task<TemporaryMapSweepReport> run;
        using (await FileKeyLock.AcquireAsync(FileA, CancellationToken.None))
        {
            run = sweep.RunOnceAsync(Now, CancellationToken.None);
            await sweepWaiting.Task.WaitAsync(HangGuard);
            Assert.That(handler.CountRequests(HttpMethod.Get, ByPath), Is.Zero, "the probe waits for the key: before it, the answer is stale");
            Assert.That(handler.CountRequests(HttpMethod.Delete, UsFile), Is.Zero, "no delete while the upload holds the key");
            Assert.That(handler.CountRequests(HttpMethod.Post, FileDeleted), Is.Zero, "no mark either");
            Assert.That(run.IsCompleted, Is.False);
        }

        var report = await run.WaitAsync(HangGuard);
        Assert.That(report.Deleted, Is.EqualTo(1));
        Assert.That(report.Failed, Is.Zero);
        Assert.That(handler.CountRequests(HttpMethod.Get, ByPath), Is.EqualTo(1));
        Assert.That(handler.CountRequests(HttpMethod.Delete, UsFile), Is.EqualTo(1));
        Assert.That(handler.CountRequests(HttpMethod.Post, FileDeleted), Is.EqualTo(1));
        Assert.That(FileKeyLock.Count, Is.Zero, "the sweep released the key");
    }

    [Test]
    public async Task AnExpiryPassFailure_DoesNotSkipTheReconciliationPass()
    {
        var handler = EmptyReconciliation()
            .On(HttpMethod.Get, Expired, HttpStatusCode.InternalServerError, "{\"errors\":[{\"param\":\"before\",\"message\":\"boom\"}]}");

        var report = await CreateSweep(handler).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(report.Failed, Is.EqualTo(1));
        Assert.That(handler.CountRequests(HttpMethod.Get, Listing), Is.EqualTo(1));
    }
}
