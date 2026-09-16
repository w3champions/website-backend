using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using NUnit.Framework;
using W3ChampionsStatisticService.Maps;

namespace WC3ChampionsStatisticService.Tests.Maps;

/// <summary>
/// The two passes of <see cref="TemporaryMapExpirySweep"/> over the real clients and one scripted handler. The clock is
/// the parameter, so nothing here waits. Every sweep purges the per-test spool directory of the base, never the
/// machine-wide one (<see cref="TemporaryMapExpirySweepSpoolTests"/> covers that pass).
/// </summary>
[TestFixture]
public class TemporaryMapExpirySweepTests : TemporaryMapUploadServiceTestBase
{
    private static readonly DateTime Now = new(2026, 9, 14, 3, 0, 0, DateTimeKind.Utc);

    /// <summary>The TTL boundary the sweep sends as <c>before</c>: a record hosted at or after it is not expired.</summary>
    private static readonly long Before = new DateTimeOffset(Now.AddDays(-TemporaryMapLimits.TtlDays)).ToUnixTimeMilliseconds();
    private const string FileA = "W3Champions/CustomGames/a-11111111.w3x";
    private const string FileB = "W3Champions/CustomGames/b-22222222.w3x";
    private const string FileC = "W3Champions/CustomGames/c-33333333.w3x";
    private const string Expired = "/maps/temporary/expired";
    private const string Listing = "/api/content/maps/files";
    private const string ByPath = "/maps/temporary/by-path";
    private const string UsFile = "/api/content/maps/file?";
    private const string FileDeleted = "/file-deleted";

    // ---- Expiry pass -------------------------------------------------------------------------

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

    // ---- Reconciliation pass -----------------------------------------------------------------

    [Test]
    public async Task ReconciliationPass_DeletesOnlyFilesNoRecordClaims()
    {
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items())
            .On(HttpMethod.Get, Listing, HttpStatusCode.OK, Files(null, FileA, FileB))
            .On(r => IsByPathOf(r, "a-11111111"), Respond(HttpStatusCode.OK, Record(1, FileA)))
            .On(HttpMethod.Get, ByPath, HttpStatusCode.NotFound)
            .On(HttpMethod.Delete, UsFile, HttpStatusCode.NoContent, "");

        var report = await CreateSweep(handler).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(report.Scanned, Is.EqualTo(2));
        Assert.That(report.ReclaimedOrphans, Is.EqualTo(1));
        Assert.That(report.Failed, Is.Zero);
        Assert.That(handler.CountRequests(HttpMethod.Delete, UsFile), Is.EqualTo(1));
        Assert.That(Uri.UnescapeDataString(handler.LastRequest(HttpMethod.Delete, UsFile).RequestUri!.Query),
            Does.Contain(FileB), "only the unclaimed file is reclaimed");
    }

    [Test]
    public async Task ReconciliationPass_OnlyLooksAtTheTemporaryPrefixAndOldEnoughFiles()
    {
        var handler = EmptyReconciliation().On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items());

        await CreateSweep(handler).RunOnceAsync(Now, CancellationToken.None);

        var query = Uri.UnescapeDataString(handler.LastRequest(HttpMethod.Get, Listing).RequestUri!.Query);
        Assert.That(query, Does.Contain($"prefix={TemporaryMapLimits.TempMapPathPrefix}"));
        Assert.That(query, Does.Contain($"olderThanHours={TemporaryMapLimits.OrphanMinAgeHours}"),
            "an upload younger than this may still be in flight and must never be reclaimed");
        Assert.That(query, Does.Contain($"limit={TemporaryMapLimits.SweepBatchSize}"));
    }

    [Test]
    public async Task ReconciliationPass_FollowsTheNextCursor()
    {
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items())
            .On(IsListing, Sequence(Files(FileA, FileA), Files(null, FileB)))
            .On(HttpMethod.Get, ByPath, HttpStatusCode.OK, Record(1));

        var report = await CreateSweep(handler).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(report.Scanned, Is.EqualTo(2));
        Assert.That(report.Failed, Is.Zero);
        Assert.That(handler.CountRequests(HttpMethod.Get, Listing), Is.EqualTo(2));
        Assert.That(Uri.UnescapeDataString(handler.LastRequest(HttpMethod.Get, Listing).RequestUri!.Query),
            Does.Contain("after=" + FileA));
    }

    [Test]
    public async Task ReconciliationPass_EndsTheRunOnARepeatingCursor_AndCountsItAsFailed()
    {
        // No page ceiling: the listing is followed until it is exhausted. A cursor that does not advance could loop
        // forever, so it ends this run's pass, loudly; the next run starts over. The rows differ per page so that this
        // is the cursor guard alone (…EndsTheRunWhenAPageOnlyRepeatsRowsAlreadyScanned covers the rows).
        using var logs = new LogCapture();
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items())
            .On(IsListing, Sequence(Files("never-ends", FileA), Files("never-ends", FileB), Files("never-ends", FileC), Files("never-ends", FileA)))
            .On(HttpMethod.Get, ByPath, HttpStatusCode.OK, Record(1, FileA));

        var report = await CreateSweep(handler, logs.CreateLogger<TemporaryMapExpirySweep>()).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(handler.CountRequests(HttpMethod.Get, Listing), Is.EqualTo(2), "the page after the cursor repeated it");
        Assert.That(report.Scanned, Is.EqualTo(2));
        Assert.That(report.Failed, Is.EqualTo(1));
        var error = logs.Lines().Single(l => l.StartsWith("Error", StringComparison.Ordinal));
        Assert.That(error, Does.Contain("cursor"));
    }

    [Test]
    public async Task ReconciliationPass_EndsTheRunOnACursorCycle()
    {
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items())
            // Cursors A, B, A again: the third page's row is fresh, so only the cursor repeats.
            .On(IsListing, Sequence(Files("A", FileA), Files("B", FileB), Files("A", FileC), Files("B", FileB)))
            .On(HttpMethod.Get, ByPath, HttpStatusCode.OK, Record(1));

        var report = await CreateSweep(handler).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(handler.CountRequests(HttpMethod.Get, Listing), Is.EqualTo(3));
        Assert.That(report.Failed, Is.EqualTo(1));
    }

    [Test]
    public async Task ReconciliationPass_ReclaimsAFileWhoseRecordSaysDeleted()
    {
        // A record that says its bytes are gone claims nothing; the bytes (a restore that never completed) are
        // reclaimed like an unclaimed file. A present record keeps its file.
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items())
            .On(HttpMethod.Get, Listing, HttpStatusCode.OK, Files(null, FileA, FileB))
            .On(r => IsByPathOf(r, "a-11111111"), Respond(HttpStatusCode.OK, Record(1, FileA, "deleted")))
            .On(r => IsByPathOf(r, "b-22222222"), Respond(HttpStatusCode.OK, Record(2, FileB, "present")))
            .On(HttpMethod.Delete, UsFile, HttpStatusCode.NoContent, "");

        var report = await CreateSweep(handler).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(report.ReclaimedOrphans, Is.EqualTo(1));
        Assert.That(report.Failed, Is.Zero);
        Assert.That(handler.CountRequests(HttpMethod.Delete, UsFile), Is.EqualTo(1));
        Assert.That(Uri.UnescapeDataString(handler.LastRequest(HttpMethod.Delete, UsFile).RequestUri!.Query), Does.Contain(FileA));
    }

    [TestCase(25, 25, 0)]
    [TestCase(26, 25, 1)]
    [TestCase(30, 25, 5)]
    public async Task ReconciliationPass_ReclaimsAtMostTheCapPerRun_AndDefersTheRestLoudly(int orphans, int expectedReclaimed, int expectedDeferred)
    {
        // Reclaiming rests on one matchmaking answer per file; a by-path regression (an inexact lookup, a spelling that
        // differs from what matchmaking stored) would call every stored file unclaimed. So one run deletes at most
        // MaxReclaimsPerRun files: the rest are counted and left for the next run — a rate bound, never a skip — and the
        // run ends with one Error line, because a backlog that large is a signal, not routine.
        using var logs = new LogCapture();
        var files = Enumerable.Range(1, orphans).Select(i => $"W3Champions/CustomGames/o-{i:D8}.w3x").ToArray();
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items())
            .On(HttpMethod.Get, Listing, HttpStatusCode.OK, Files(null, files))
            .On(HttpMethod.Get, ByPath, HttpStatusCode.NotFound)
            .On(HttpMethod.Delete, UsFile, HttpStatusCode.NoContent, "");

        var report = await CreateSweep(handler, logs.CreateLogger<TemporaryMapExpirySweep>()).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(report.Scanned, Is.EqualTo(orphans), "every file is still examined");
        Assert.That(report.ReclaimedOrphans, Is.EqualTo(expectedReclaimed));
        Assert.That(report.Deferred, Is.EqualTo(expectedDeferred));
        Assert.That(report.Failed, Is.Zero, "a deferred reclaim is not a failure");
        Assert.That(handler.CountRequests(HttpMethod.Delete, UsFile), Is.EqualTo(expectedReclaimed));
        var deleted = handler.Requests.Where(r => r.Method == HttpMethod.Delete).Select(r => Uri.UnescapeDataString(r.RequestUri!.Query)).ToArray();
        Assert.That(files.Take(expectedReclaimed).All(f => deleted.Any(q => q.Contains(f))), Is.True, "the first files in listing order");
        Assert.That(files.Skip(expectedReclaimed).Any(f => deleted.Any(q => q.Contains(f))), Is.False, "nothing past the cap");
        var errors = logs.Lines().Where(l => l.StartsWith("Error", StringComparison.Ordinal)).ToArray();
        if (expectedDeferred == 0)
        {
            Assert.That(errors, Is.Empty);
        }
        else
        {
            Assert.That(errors, Has.Length.EqualTo(1), "one line for the whole pass");
            Assert.That(errors[0], Does.Contain($"MaxReclaimsPerRun={TemporaryMapLimits.MaxReclaimsPerRun}").And.Contain($"Deferred={expectedDeferred}"));
        }

        Assert.That(TemporaryMapLimits.MaxReclaimsPerRun, Is.EqualTo(25));
    }

    [Test]
    public async Task ReconciliationPass_TheCapCountsReclaimsOfBothKinds_AndSkipsNothingElse()
    {
        // Unclaimed files and files whose record says deleted share the cap; a file a present record claims is neither
        // reclaimed nor deferred, and a failure past the cap is still a failure.
        using var logs = new LogCapture();
        var unclaimed = Enumerable.Range(1, 20).Select(i => $"W3Champions/CustomGames/u-{i:D8}.w3x").ToArray();
        var released = Enumerable.Range(1, 6).Select(i => $"W3Champions/CustomGames/r-{i:D8}.w3x").ToArray();
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items())
            .On(HttpMethod.Get, Listing, HttpStatusCode.OK, Files(null, [.. unclaimed, .. released, FileA, FileB]))
            .On(r => IsByPathFor(r, FileA), Respond(HttpStatusCode.OK, Record(1, FileA)))
            .On(r => IsByPathFor(r, FileB), TransportFails())
            .On(r => IsByPathProbe(r) && PathQueryOf(r).Contains("/r-", StringComparison.Ordinal), r => ScriptedHttpHandler.Json(HttpStatusCode.OK, Record(7, PathQueryOf(r), "deleted")))
            .On(HttpMethod.Get, ByPath, HttpStatusCode.NotFound)
            .On(HttpMethod.Delete, UsFile, HttpStatusCode.NoContent, "");

        var report = await CreateSweep(handler, logs.CreateLogger<TemporaryMapExpirySweep>()).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(report.Scanned, Is.EqualTo(28));
        Assert.That(report.ReclaimedOrphans, Is.EqualTo(TemporaryMapLimits.MaxReclaimsPerRun));
        Assert.That(report.Deferred, Is.EqualTo(1), "the last released file");
        Assert.That(report.Failed, Is.EqualTo(1), "the file whose probe failed");
        Assert.That(handler.CountRequests(HttpMethod.Delete, UsFile), Is.EqualTo(TemporaryMapLimits.MaxReclaimsPerRun));
        Assert.That(handler.CountRequests(HttpMethod.Get, ByPath), Is.EqualTo(28), "every file is still examined");
    }

    [TestCase("W3Champions/CustomGames/e\u0301-11111111.w3x", TestName = "ReconciliationPass_RefusesAListedPathThatIsNotNfc")]
    [TestCase("W3Champions/CustomGames/sub/x-11111111.w3x", TestName = "ReconciliationPass_RefusesAListedPathWithASubdirectory")]
    [TestCase("W3Champions/CustomGames/x.w3x", TestName = "ReconciliationPass_RefusesAListedPathWithoutTheSha1Suffix")]
    [TestCase("W3Champions/CustomGames/x-1111111G.w3x", TestName = "ReconciliationPass_RefusesAListedPathWithANonHexSuffix")]
    [TestCase("W3Champions/CustomGames/x-11111111.txt", TestName = "ReconciliationPass_RefusesAListedPathWithAnotherExtension")]
    [TestCase("W3Champions/CustomGames/-11111111.w3x", TestName = "ReconciliationPass_RefusesAListedPathWithAnEmptyStem")]
    public async Task ReconciliationPass_ReclaimsOnlyAPathSpelledExactlyAsThisServiceWouldHaveBuiltIt(string listedPath)
    {
        // The lock key and the by-path lookup both use the listing's spelling; a spelling this service never produces
        // (a decomposed form of a name it stored composed, a shape BuildFileKey cannot emit) is where a by-path answer
        // of "unclaimed" is least trustworthy. Such a file is a failure of the run: not asked about, not deleted.
        using var logs = new LogCapture();
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items())
            .On(HttpMethod.Get, Listing, HttpStatusCode.OK, Files(null, listedPath, FileB))
            .On(HttpMethod.Get, ByPath, HttpStatusCode.NotFound)
            .On(HttpMethod.Delete, UsFile, HttpStatusCode.NoContent, "");

        var report = await CreateSweep(handler, logs.CreateLogger<TemporaryMapExpirySweep>()).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(report.Scanned, Is.EqualTo(2));
        Assert.That(report.Failed, Is.EqualTo(1));
        Assert.That(report.ReclaimedOrphans, Is.EqualTo(1), "the well-formed file is still reclaimed");
        Assert.That(handler.CountRequests(HttpMethod.Get, ByPath), Is.EqualTo(1), "only the well-formed file was probed");
        Assert.That(handler.CountRequests(HttpMethod.Delete, UsFile), Is.EqualTo(1));
        Assert.That(Uri.UnescapeDataString(handler.LastRequest(HttpMethod.Delete, UsFile).RequestUri!.Query), Does.Contain(FileB));
        Assert.That(logs.Lines().Single(l => l.StartsWith("Warning", StringComparison.Ordinal)), Does.Contain("not reclaimed"));
    }

    [Test]
    public async Task ReconciliationPass_ProbesAndDeletesUnderTheFileKeyLock_AnUploadHolds()
    {
        // An upload of the same fileKey holds the lock from its store to its record write. A probe before that write
        // would answer "unclaimed" and a delete would take the upload's bytes, so neither happens until the upload is done.
        var sweepWaiting = NewSignal();
        FileKeyLock = new TemporaryMapFileKeyLock { OnContended = _ => sweepWaiting.TrySetResult() };
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items())
            .On(HttpMethod.Get, Listing, HttpStatusCode.OK, Files(null, FileA))
            .On(HttpMethod.Get, ByPath, HttpStatusCode.NotFound)
            .On(HttpMethod.Delete, UsFile, HttpStatusCode.NoContent, "");
        var sweep = CreateSweep(handler);

        Task<TemporaryMapSweepReport> run;
        using (await FileKeyLock.AcquireAsync(FileA, CancellationToken.None))
        {
            run = sweep.RunOnceAsync(Now, CancellationToken.None);
            await sweepWaiting.Task.WaitAsync(HangGuard);
            Assert.That(handler.CountRequests(HttpMethod.Get, Listing), Is.EqualTo(1), "the listing needs no key");
            Assert.That(handler.CountRequests(HttpMethod.Get, ByPath), Is.Zero, "the probe waits for the key: before it, the answer is stale");
            Assert.That(handler.CountRequests(HttpMethod.Delete, UsFile), Is.Zero, "no delete while the upload holds the key");
            Assert.That(run.IsCompleted, Is.False);
        }

        var report = await run.WaitAsync(HangGuard);
        Assert.That(report.ReclaimedOrphans, Is.EqualTo(1));
        Assert.That(report.Failed, Is.Zero);
        Assert.That(handler.CountRequests(HttpMethod.Get, ByPath), Is.EqualTo(1));
        Assert.That(handler.CountRequests(HttpMethod.Delete, UsFile), Is.EqualTo(1));
        Assert.That(FileKeyLock.Count, Is.Zero, "the sweep released the key");
    }

    [TestCase(FileB)]
    [TestCase("W3Champions/CustomGames/A-11111111.w3x")]
    public async Task ReconciliationPass_ADeletedRecordThatNamesAnotherPath_IsAFailure_NotAnOrphan(string recordPath)
    {
        // A record reached by an inexact lookup (another file, another case) says nothing about this file.
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items())
            .On(HttpMethod.Get, Listing, HttpStatusCode.OK, Files(null, FileA))
            .On(HttpMethod.Get, ByPath, HttpStatusCode.OK, Record(1, recordPath, "deleted"))
            .On(HttpMethod.Delete, UsFile, HttpStatusCode.NoContent, "");

        var report = await CreateSweep(handler).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(report.Failed, Is.EqualTo(1));
        Assert.That(report.ReclaimedOrphans, Is.Zero);
        Assert.That(handler.CountRequests(HttpMethod.Delete, UsFile), Is.Zero);
    }

    [Test]
    public async Task ReconciliationPass_EndsTheRunWhenAPageOnlyRepeatsRowsAlreadyScanned()
    {
        // A listing that ignores `after` yet mints fresh cursors never repeats a cursor; without this guard it
        // would loop forever holding the run lock.
        using var logs = new LogCapture();
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items())
            .On(IsListing, Sequence(Files("c1", FileA, FileB), Files("c2", FileA, FileB), Files("c3", FileA, FileB)))
            .On(r => IsByPathOf(r, "a-11111111"), Respond(HttpStatusCode.OK, Record(1, FileA)))
            .On(r => IsByPathOf(r, "b-22222222"), Respond(HttpStatusCode.OK, Record(2, FileB)));

        var report = await CreateSweep(handler, logs.CreateLogger<TemporaryMapExpirySweep>()).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(handler.CountRequests(HttpMethod.Get, Listing), Is.EqualTo(2));
        Assert.That(report.Scanned, Is.EqualTo(2));
        Assert.That(report.Failed, Is.EqualTo(1));
        Assert.That(handler.CountRequests(HttpMethod.Get, ByPath), Is.EqualTo(2), "each file was examined once");
        Assert.That(logs.Lines().Single(l => l.StartsWith("Error", StringComparison.Ordinal)), Does.Contain("rows"));
    }

    [Test]
    public async Task ReconciliationPass_ExaminesARowRepeatedOnALaterPage_OnceARun()
    {
        // A page that overlaps the previous one (a listing whose cursor is inclusive) still advances: only the new rows
        // are examined, and the run goes on to the end.
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items())
            .On(IsListing, Sequence(Files("c1", FileA, FileB), Files(null, FileB, FileC)))
            .On(HttpMethod.Get, ByPath, HttpStatusCode.NotFound)
            .On(HttpMethod.Delete, UsFile, HttpStatusCode.NoContent, "");

        var report = await CreateSweep(handler).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(report.Scanned, Is.EqualTo(3));
        Assert.That(report.ReclaimedOrphans, Is.EqualTo(3));
        Assert.That(report.Failed, Is.Zero);
        Assert.That(handler.CountRequests(HttpMethod.Get, ByPath), Is.EqualTo(3), "B was examined once");
    }

    [Test]
    public async Task ReconciliationPass_FollowsAnEmptyPageThatStillHasACursor()
    {
        // update-service may filter a page down to nothing (every file on it younger than olderThanHours) and still have
        // more behind it: not stuck, just empty.
        using var logs = new LogCapture();
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items())
            .On(IsListing, Sequence(Files("c1"), Files("c2"), Files(null, FileA)))
            .On(HttpMethod.Get, ByPath, HttpStatusCode.OK, Record(1));

        var report = await CreateSweep(handler, logs.CreateLogger<TemporaryMapExpirySweep>()).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(handler.CountRequests(HttpMethod.Get, Listing), Is.EqualTo(3));
        Assert.That(report.Scanned, Is.EqualTo(1));
        Assert.That(report.Failed, Is.Zero);
        Assert.That(logs.Lines(), Has.None.StartsWith("Error"));
    }

    [Test]
    public async Task ReconciliationPass_ALastPageThatOnlyOverlapsThePrevious_IsTheNormalEnd()
    {
        // An inclusive cursor whose last page holds nothing but the overlap: the listing is exhausted, not stuck.
        using var logs = new LogCapture();
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items())
            .On(IsListing, Sequence(Files("c1", FileA, FileB), Files(null, FileB)))
            .On(HttpMethod.Get, ByPath, HttpStatusCode.OK, Record(1));

        var report = await CreateSweep(handler, logs.CreateLogger<TemporaryMapExpirySweep>()).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(report.Scanned, Is.EqualTo(2));
        Assert.That(report.Failed, Is.Zero);
        Assert.That(logs.Lines(), Has.None.StartsWith("Error"));
    }

    [Test]
    public async Task ReconciliationPass_ARouteStyle404_IsAFailure_NotAnOrphan()
    {
        // Only the empty-object 404 means "no record". Anything else is the route missing (matchmaking
        // deployed without it, a proxy page) and reading it as unclaimed would delete every listed file.
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items())
            .On(HttpMethod.Get, Listing, HttpStatusCode.OK, Files(null, FileA))
            .On(HttpMethod.Get, ByPath, HttpStatusCode.NotFound, "{\"message\":\"Cannot GET /maps/temporary/by-path\"}")
            .On(HttpMethod.Delete, UsFile, HttpStatusCode.NoContent, "");

        var report = await CreateSweep(handler).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(report.Scanned, Is.EqualTo(1));
        Assert.That(report.ReclaimedOrphans, Is.Zero);
        Assert.That(report.Failed, Is.EqualTo(1));
        Assert.That(handler.CountRequests(HttpMethod.Delete, UsFile), Is.Zero);
    }

    [TestCase("archived")]
    [TestCase("")]
    public async Task ReconciliationPass_AnUnknownFileState_IsAFailure_NotAnOrphan(string fileState)
    {
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items())
            .On(HttpMethod.Get, Listing, HttpStatusCode.OK, Files(null, FileA))
            .On(HttpMethod.Get, ByPath, HttpStatusCode.OK, Record(1, FileA, fileState))
            .On(HttpMethod.Delete, UsFile, HttpStatusCode.NoContent, "");

        var report = await CreateSweep(handler).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(report.ReclaimedOrphans, Is.Zero);
        Assert.That(report.Failed, Is.EqualTo(1));
        Assert.That(handler.CountRequests(HttpMethod.Delete, UsFile), Is.Zero);
    }

    [Test]
    public async Task ReconciliationPass_AByPathTimeout_IsThatFilesFailure_AndTheNextFileIsStillExamined()
    {
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items())
            .On(HttpMethod.Get, Listing, HttpStatusCode.OK, Files(null, FileA, FileB))
            .On(r => IsByPathOf(r, "a-11111111"), TimesOut())
            .On(HttpMethod.Get, ByPath, HttpStatusCode.NotFound)
            .On(HttpMethod.Delete, UsFile, HttpStatusCode.NoContent, "");

        var report = await CreateSweep(handler).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(report.Scanned, Is.EqualTo(2));
        Assert.That(report.Failed, Is.EqualTo(1));
        Assert.That(report.ReclaimedOrphans, Is.EqualTo(1));
        Assert.That(Uri.UnescapeDataString(handler.LastRequest(HttpMethod.Delete, UsFile).RequestUri!.Query), Does.Contain(FileB));
    }

    [Test]
    public async Task ReconciliationPass_AListingFailure_EndsThePass_AndCountsAsFailed()
    {
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items())
            .On(HttpMethod.Get, Listing, HttpStatusCode.BadGateway, "<html>502</html>");

        var report = await CreateSweep(handler).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(report.Scanned, Is.Zero);
        Assert.That(report.Failed, Is.EqualTo(1));
        Assert.That(handler.CountRequests(HttpMethod.Get, ByPath), Is.Zero);
    }

    [Test]
    public async Task ReconciliationPass_AListedFileOutsideTheTemporaryPrefix_IsNeverAskedAboutOrDeleted()
    {
        // update-service drift: the listing was asked for CustomGames/ only. Such a file is a failure of this run
        // (loud, retried), not a candidate — the delete guard would refuse it anyway, but it is not even probed.
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items())
            .On(HttpMethod.Get, Listing, HttpStatusCode.OK, Files(null, "W3Champions/Maps/echo-isles.w3x", FileB))
            .On(HttpMethod.Get, ByPath, HttpStatusCode.NotFound)
            .On(HttpMethod.Delete, UsFile, HttpStatusCode.NoContent, "");

        var report = await CreateSweep(handler).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(report.Scanned, Is.EqualTo(2));
        Assert.That(report.Failed, Is.EqualTo(1));
        Assert.That(report.ReclaimedOrphans, Is.EqualTo(1));
        Assert.That(handler.CountRequests(HttpMethod.Get, ByPath), Is.EqualTo(1), "only the CustomGames/ file was probed");
        Assert.That(handler.CountRequests(HttpMethod.Delete, UsFile), Is.EqualTo(1));
        Assert.That(Uri.UnescapeDataString(handler.LastRequest(HttpMethod.Delete, UsFile).RequestUri!.Query), Does.Contain(FileB));
    }

    // ---- The run -----------------------------------------------------------------------------

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

    // ---- Helpers -----------------------------------------------------------------------------

    private static bool IsExpired(HttpRequestMessage r)
        => r.Method == HttpMethod.Get && r.RequestUri!.PathAndQuery.Contains(Expired, StringComparison.Ordinal);

    private static bool IsListing(HttpRequestMessage r)
        => r.Method == HttpMethod.Get && r.RequestUri!.PathAndQuery.Contains(Listing, StringComparison.Ordinal);

    private static bool IsByPathOf(HttpRequestMessage r, string fileNamePart)
        => r.Method == HttpMethod.Get && r.RequestUri!.PathAndQuery.Contains(ByPath, StringComparison.Ordinal)
           && r.RequestUri.Query.Contains(fileNamePart, StringComparison.Ordinal);

    private static bool IsByPathProbe(HttpRequestMessage r)
        => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.EndsWith(ByPath, StringComparison.Ordinal);

    /// <summary>The decoded <c>path</c> the by-path probe asked about.</summary>
    private static string PathQueryOf(HttpRequestMessage r) => QueryHelpers.ParseQuery(r.RequestUri!.Query)["path"].ToString();

    private static bool IsByPathFor(HttpRequestMessage r, string path)
        => IsByPathProbe(r) && string.Equals(PathQueryOf(r), path, StringComparison.Ordinal);

    /// <summary>
    /// The by-path answer that confirms an expiry item: the record of that id, present at that very path, last hosted
    /// before the TTL boundary (<paramref name="lastHostedAt"/> defaults to one millisecond before it).
    /// </summary>
    private static string Claim(int id, string path, string fileState = "present", long? lastHostedAt = long.MinValue)
    {
        var hosted = lastHostedAt == long.MinValue ? Before - 1 : lastHostedAt;
        return "{\"map\":{\"id\":" + id + ",\"name\":\"m\",\"path\":\"" + path + "\",\"temporary\":true,\"fileState\":\"" + fileState + "\"" +
               (hosted == null ? "" : ",\"lastHostedAt\":" + hosted) + "}}";
    }

    /// <summary>One by-path responder confirming each of <paramref name="items"/> as expired; any other path is unclaimed.</summary>
    private static Func<HttpRequestMessage, HttpResponseMessage> ClaimsOf(params (int Id, string Path)[] items)
    {
        var byPath = items.ToDictionary(i => i.Path, i => i.Id, StringComparer.Ordinal);
        return r =>
        {
            var path = PathQueryOf(r);
            return byPath.TryGetValue(path, out var id)
                ? ScriptedHttpHandler.Json(HttpStatusCode.OK, Claim(id, path))
                : ScriptedHttpHandler.Json(HttpStatusCode.NotFound, "{}");
        };
    }

    private static string SweepRoute(HttpRequestMessage r)
        => IsExpired(r) ? "expired" : IsListing(r) ? "files" : r.RequestUri!.AbsolutePath;

    private static (int Id, string Path)[] Range(int firstId, int count)
        => Enumerable.Range(firstId, count).Select(id => (id, $"W3Champions/CustomGames/m-{id:D8}.w3x")).ToArray();

    /// <summary>The GET /maps/temporary/expired page holding these records.</summary>
    private static string Items(params (int Id, string Path)[] items)
        => "{\"items\":[" + string.Join(",", items.Select(i => "{\"id\":" + i.Id + ",\"path\":\"" + i.Path + "\"}")) + "]}";

    /// <summary>The GET /api/content/maps/files page holding these files and cursor.</summary>
    private static string Files(string next, params string[] filePaths)
    {
        var files = string.Join(",", filePaths.Select(p => "{\"filePath\":\"" + p + "\",\"sizeBytes\":1,\"modifiedAt\":\"2026-09-01T00:00:00Z\"}"));
        return "{\"files\":[" + files + "],\"next\":" + (next == null ? "null" : "\"" + next + "\"") + "}";
    }

    private static ScriptedHttpHandler EmptyReconciliation()
        => new ScriptedHttpHandler().On(HttpMethod.Get, Listing, HttpStatusCode.OK, Files(null));

    /// <summary>
    /// One 200 page per call, in order, and a failure once they run out: a loop that stops where it should never asks
    /// for more, and one that does not stop fails the test instead of spinning on a route that always answers.
    /// </summary>
    private static Func<HttpRequestMessage, HttpResponseMessage> Sequence(params string[] pages)
    {
        var remaining = new Queue<string>(pages);
        return _ => remaining.Count > 0
            ? ScriptedHttpHandler.Json(HttpStatusCode.OK, remaining.Dequeue())
            : throw new InvalidOperationException("the sweep asked for more pages than the test scripted");
    }
}
