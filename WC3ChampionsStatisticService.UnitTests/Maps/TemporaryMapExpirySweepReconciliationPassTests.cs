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
/// The reconciliation pass of <see cref="TemporaryMapExpirySweep"/>: which listed files are reclaimed, the per-run cap,
/// the spelling gate, the cursor loop and per-file isolation. Shared scripting lives in
/// <see cref="TemporaryMapExpirySweepTestBase"/>.
/// </summary>
[TestFixture]
public class TemporaryMapExpirySweepReconciliationPassTests : TemporaryMapExpirySweepTestBase
{
    [Test]
    public async Task ReconciliationPass_DeletesOnlyFilesNoRecordClaims()
    {
        var handler = ReclaimsAreVerified()
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
        var handler = ReclaimsAreVerified()
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
        var handler = ReclaimsAreVerified()
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
        var handler = ReclaimsAreVerified()
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
        var handler = ReclaimsAreVerified()
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
        var handler = ReclaimsAreVerified()
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
        var handler = ReclaimsAreVerified()
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
        var handler = ReclaimsAreVerified()
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
        var handler = ReclaimsAreVerified()
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
        var handler = ReclaimsAreVerified()
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
        var handler = ReclaimsAreVerified()
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
        var handler = ReclaimsAreVerified()
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
        var handler = ReclaimsAreVerified()
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
        var handler = ReclaimsAreVerified()
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
        var handler = ReclaimsAreVerified()
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
        var handler = ReclaimsAreVerified()
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
        var handler = ReclaimsAreVerified()
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
        var handler = ReclaimsAreVerified()
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
        var handler = ReclaimsAreVerified()
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

    // ---- A file update-service keeps is not a reclaimed orphan -------------------------------

    [Test]
    public async Task ReconciliationPass_CountsAReclaimOnceTheProbeShowsThePathGone()
    {
        using var logs = new LogCapture();
        const string orphan = "W3Champions/CustomGames/gone-11111111.w3x";
        var handler = ReclaimsAreVerified()
            .On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items())
            .On(HttpMethod.Get, Listing, HttpStatusCode.OK, Files(null, orphan))
            .On(HttpMethod.Get, ByPath, HttpStatusCode.NotFound)
            .On(HttpMethod.Delete, UsFile, HttpStatusCode.NoContent, "");

        var report = await CreateSweep(handler, logs.CreateLogger<TemporaryMapExpirySweep>()).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(report.ReclaimedOrphans, Is.EqualTo(1));
        Assert.That(report.ProtectedFiles, Is.Zero);
        Assert.That(report.Failed, Is.Zero);
        Assert.That(logs.Lines().Single(l => l.Contains("ORPHAN_RECLAIMED")), Does.Contain(orphan));
        Assert.That(logs.Lines(), Has.None.StartsWith("Warning"));
    }

    [Test]
    public async Task ReconciliationPass_AFileUpdateServiceKeptAfterTheDelete_IsProtected_NotReclaimed()
    {
        // update-service never keys an unkeyed legacy row at a temporary path, so its by-path delete for one is a
        // logged no-op and the bytes stay. Counted as a reclaim it would be a false success on every run, forever.
        using var logs = new LogCapture();
        const string kept = "W3Champions/CustomGames/kept-22222222.w3x";
        var handler = new ScriptedHttpHandler()
            .On(IsReclaimProbe, StillStored(kept))
            .On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items())
            .On(HttpMethod.Get, Listing, HttpStatusCode.OK, Files(null, kept))
            .On(HttpMethod.Get, ByPath, HttpStatusCode.NotFound)
            .On(HttpMethod.Delete, UsFile, HttpStatusCode.NoContent, "");

        var report = await CreateSweep(handler, logs.CreateLogger<TemporaryMapExpirySweep>()).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(report.ReclaimedOrphans, Is.Zero);
        Assert.That(report.ProtectedFiles, Is.EqualTo(1));
        Assert.That(report.Deferred, Is.Zero);
        Assert.That(report.Failed, Is.Zero, "update-service accepted the delete; nothing failed");
        Assert.That(handler.CountRequests(HttpMethod.Delete, UsFile), Is.EqualTo(1));
        Assert.That(logs.Lines().Any(l => l.Contains("ORPHAN_RECLAIMED")), Is.False);
        var warnings = logs.Lines().Where(l => l.StartsWith("Warning", StringComparison.Ordinal)).ToArray();
        Assert.That(warnings, Has.Length.EqualTo(1), "one Warning names the path");
        Assert.That(warnings[0], Does.Contain(kept).And.Contain("still stored"));
    }

    [Test]
    public async Task ReconciliationPass_AProbeAnsweringTheNextStoredPath_MeansThisOneIsGone()
    {
        // The probe asks for one row after the bound, so a genuinely reclaimed file's probe answers the NEXT stored
        // file rather than an empty page. Only a row equal to the deleted path means update-service kept it.
        const string orphan = "W3Champions/CustomGames/next-55555555.w3x";
        const string neighbour = "W3Champions/CustomGames/next-66666666.w3x";
        var handler = new ScriptedHttpHandler()
            .On(IsReclaimProbe, StillStored(neighbour))
            .On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items())
            .On(HttpMethod.Get, Listing, HttpStatusCode.OK, Files(null, orphan))
            .On(HttpMethod.Get, ByPath, HttpStatusCode.NotFound)
            .On(HttpMethod.Delete, UsFile, HttpStatusCode.NoContent, "");

        var report = await CreateSweep(handler).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(report.ReclaimedOrphans, Is.EqualTo(1));
        Assert.That(report.ProtectedFiles, Is.Zero);
        Assert.That(report.Failed, Is.Zero);
    }

    [Test]
    public async Task ReconciliationPass_WarnsOncePerProtectedPath_ButEscalatesTheCountEveryRun()
    {
        // The Warning is spent on the first sighting in this process, so the per-run Error line is what keeps the
        // condition visible afterwards.
        using var logs = new LogCapture();
        const string kept = "W3Champions/CustomGames/twice-33333333.w3x";
        var handler = new ScriptedHttpHandler()
            .On(IsReclaimProbe, StillStored(kept))
            .On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items())
            .On(HttpMethod.Get, Listing, HttpStatusCode.OK, Files(null, kept))
            .On(HttpMethod.Get, ByPath, HttpStatusCode.NotFound)
            .On(HttpMethod.Delete, UsFile, HttpStatusCode.NoContent, "");
        var sweep = CreateSweep(handler, logs.CreateLogger<TemporaryMapExpirySweep>());

        var first = await sweep.RunOnceAsync(Now, CancellationToken.None);
        var second = await sweep.RunOnceAsync(Now, CancellationToken.None);

        Assert.That(first.ProtectedFiles, Is.EqualTo(1));
        Assert.That(second.ProtectedFiles, Is.EqualTo(1));
        Assert.That(logs.Lines().Count(l => l.StartsWith("Warning", StringComparison.Ordinal) && l.Contains(kept)), Is.EqualTo(1),
            "the path is named once per process, not once per run");
        Assert.That(logs.Lines().Count(l => l.StartsWith("Error", StringComparison.Ordinal) && l.Contains("ProtectedFiles=1")), Is.EqualTo(2),
            "the count is escalated on every run");
    }

    [Test]
    public async Task ReconciliationPass_ProbesFromThePathImmediatelyBeforeTheOneItDeleted()
    {
        // The probe asks for one row strictly after a bound: the preceding listed path, or the page's own cursor for
        // its first row. A looser bound could answer with another file and read a kept file as gone.
        const string first = "W3Champions/CustomGames/bound-a1111111.w3x";
        const string second = "W3Champions/CustomGames/bound-b2222222.w3x";
        const string third = "W3Champions/CustomGames/bound-c3333333.w3x";
        var handler = ReclaimsAreVerified()
            .On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items())
            .On(IsListing, Sequence(Files("page-1-cursor", first), Files(null, second, third)))
            .On(HttpMethod.Get, ByPath, HttpStatusCode.NotFound)
            .On(HttpMethod.Delete, UsFile, HttpStatusCode.NoContent, "");

        var report = await CreateSweep(handler).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(report.ReclaimedOrphans, Is.EqualTo(3));
        Assert.That(handler.Requests.Where(IsReclaimProbe).Select(AfterOf).ToArray(),
            Is.EqualTo(new[] { null, "page-1-cursor", second }));
    }

    [Test]
    public async Task ReconciliationPass_AProbeFailureAfterTheDelete_IsThatFilesFailure()
    {
        // The reclaim delete is idempotent, so the next run simply does it again; this run counts the file as neither
        // reclaimed nor protected.
        using var logs = new LogCapture();
        const string orphan = "W3Champions/CustomGames/probe-44444444.w3x";
        var handler = new ScriptedHttpHandler()
            .On(IsReclaimProbe, TransportFails())
            .On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items())
            .On(HttpMethod.Get, Listing, HttpStatusCode.OK, Files(null, orphan))
            .On(HttpMethod.Get, ByPath, HttpStatusCode.NotFound)
            .On(HttpMethod.Delete, UsFile, HttpStatusCode.NoContent, "");

        var report = await CreateSweep(handler, logs.CreateLogger<TemporaryMapExpirySweep>()).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(report.Failed, Is.EqualTo(1));
        Assert.That(report.ReclaimedOrphans, Is.Zero);
        Assert.That(report.ProtectedFiles, Is.Zero);
        Assert.That(handler.CountRequests(HttpMethod.Delete, UsFile), Is.EqualTo(1));
        Assert.That(logs.Lines().Single(l => l.StartsWith("Warning", StringComparison.Ordinal)),
            Does.Contain("retrying next run").And.Contain(orphan));
    }

    [Test]
    public async Task ReconciliationPass_TheCapCountsProtectedFilesToo_SoTheyReachTheDeferredEscalation()
    {
        // A protected file consumed a delete attempt, so it counts against the run's blast radius; otherwise a
        // directory full of them would sit at the cap every run without ever reaching the Deferred escalation.
        using var logs = new LogCapture();
        var all = Enumerable.Range(1, TemporaryMapLimits.MaxReclaimsPerRun + 1)
            .Select(i => $"W3Champions/CustomGames/cap{i:D2}-{i:D8}.w3x").ToArray();
        var kept = all.Where((_, index) => index % 3 == 2).ToHashSet(StringComparer.Ordinal);
        var handler = new ScriptedHttpHandler()
            .On(IsReclaimProbe, r =>
            {
                var after = AfterOf(r);
                var path = all[after == null ? 0 : Array.IndexOf(all, after) + 1];
                return ScriptedHttpHandler.Json(HttpStatusCode.OK, kept.Contains(path) ? Files(null, path) : Files(null));
            })
            .On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items())
            .On(HttpMethod.Get, Listing, HttpStatusCode.OK, Files(null, all))
            .On(HttpMethod.Get, ByPath, HttpStatusCode.NotFound)
            .On(HttpMethod.Delete, UsFile, HttpStatusCode.NoContent, "");

        var report = await CreateSweep(handler, logs.CreateLogger<TemporaryMapExpirySweep>()).RunOnceAsync(Now, CancellationToken.None);

        var expectedProtected = all.Take(TemporaryMapLimits.MaxReclaimsPerRun).Count(kept.Contains);
        Assert.That(report.ProtectedFiles, Is.EqualTo(expectedProtected));
        Assert.That(report.ReclaimedOrphans, Is.EqualTo(TemporaryMapLimits.MaxReclaimsPerRun - expectedProtected));
        Assert.That(report.Deferred, Is.EqualTo(1), "the candidate past the cap");
        Assert.That(report.Failed, Is.Zero);
        Assert.That(handler.CountRequests(HttpMethod.Delete, UsFile), Is.EqualTo(TemporaryMapLimits.MaxReclaimsPerRun),
            "nothing past the cap was deleted");
        var errors = logs.Lines().Where(l => l.StartsWith("Error", StringComparison.Ordinal)).ToArray();
        Assert.That(errors.Count(l => l.Contains($"Deferred={report.Deferred}")), Is.EqualTo(1));
        Assert.That(errors.Count(l => l.Contains($"ProtectedFiles={expectedProtected}")), Is.EqualTo(1));
    }

    [Test]
    public async Task ReconciliationPass_ACandidateWhoseProbeThrows_StillSpendsItsCapSlot()
    {
        // The cap bounds DELETE ATTEMPTS, not verified outcomes. A probe that throws leaves the file counted as
        // neither reclaimed nor protected, so a cap gated on those two counters would never advance and an
        // intermittently failing probe would let one run delete every candidate it walked.
        var candidates = Enumerable.Range(1, TemporaryMapLimits.MaxReclaimsPerRun + 1)
            .Select(i => $"W3Champions/CustomGames/att{i:D2}-{i:D8}.w3x").ToArray();
        var handler = new ScriptedHttpHandler()
            .On(IsReclaimProbe, TransportFails())
            .On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items())
            .On(HttpMethod.Get, Listing, HttpStatusCode.OK, Files(null, candidates))
            .On(HttpMethod.Get, ByPath, HttpStatusCode.NotFound)
            .On(HttpMethod.Delete, UsFile, HttpStatusCode.NoContent, "");

        var report = await CreateSweep(handler).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(handler.CountRequests(HttpMethod.Delete, UsFile), Is.EqualTo(TemporaryMapLimits.MaxReclaimsPerRun),
            "the run's delete blast radius stays bounded although nothing could be verified");
        Assert.That(report.DeleteAttempts, Is.EqualTo(TemporaryMapLimits.MaxReclaimsPerRun));
        Assert.That(report.Failed, Is.EqualTo(TemporaryMapLimits.MaxReclaimsPerRun), "every attempt's probe failed");
        Assert.That(report.Deferred, Is.EqualTo(1), "the candidate past the cap; deferring is not a failure");
        Assert.That(report.ReclaimedOrphans, Is.Zero);
        Assert.That(report.ProtectedFiles, Is.Zero);
    }

    [Test]
    public async Task ReconciliationPass_ACandidateWhoseDeleteThrows_StillSpendsItsCapSlot()
    {
        // A delete that throws may still have reached the volume — an HttpClient timeout says nothing about what the
        // server did — so the attempt is spent before the call, not after it returns.
        var candidates = Enumerable.Range(1, TemporaryMapLimits.MaxReclaimsPerRun + 1)
            .Select(i => $"W3Champions/CustomGames/thr{i:D2}-{i:D8}.w3x").ToArray();
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items())
            .On(HttpMethod.Get, Listing, HttpStatusCode.OK, Files(null, candidates))
            .On(HttpMethod.Get, ByPath, HttpStatusCode.NotFound)
            .On(IsUsDelete, TimesOut());

        var report = await CreateSweep(handler).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(handler.CountRequests(HttpMethod.Delete, UsFile), Is.EqualTo(TemporaryMapLimits.MaxReclaimsPerRun));
        Assert.That(report.DeleteAttempts, Is.EqualTo(TemporaryMapLimits.MaxReclaimsPerRun));
        Assert.That(report.Failed, Is.EqualTo(TemporaryMapLimits.MaxReclaimsPerRun));
        Assert.That(report.Deferred, Is.EqualTo(1));
        Assert.That(report.ReclaimedOrphans, Is.Zero);
        Assert.That(report.ProtectedFiles, Is.Zero);
        Assert.That(handler.CountRequests(HttpMethod.Get, Listing), Is.EqualTo(1), "a delete that threw is never verified");
    }

    [Test]
    public async Task ReconciliationPass_AProbeAnsweringAPathBelowThisOne_IsInconclusive_NotAReclaim()
    {
        // update-service recomputes its olderThanHours cutoff per request, so a file just under the age bound when
        // the page was listed can age into the window before the probe and sort between the bound and this path.
        // The probe then learned nothing about this path: counting it reclaimed is the very lie this round removes.
        using var logs = new LogCapture();
        const string orphan = "W3Champions/CustomGames/aged-77777777.w3x";
        const string agedIn = "W3Champions/CustomGames/aged-66666666.w3x";
        var handler = new ScriptedHttpHandler()
            .On(IsReclaimProbe, StillStored(agedIn))
            .On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items())
            .On(HttpMethod.Get, Listing, HttpStatusCode.OK, Files(null, orphan))
            .On(HttpMethod.Get, ByPath, HttpStatusCode.NotFound)
            .On(HttpMethod.Delete, UsFile, HttpStatusCode.NoContent, "");

        var report = await CreateSweep(handler, logs.CreateLogger<TemporaryMapExpirySweep>()).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(report.ReclaimedOrphans, Is.Zero, "a row below this path says nothing about it");
        Assert.That(report.ProtectedFiles, Is.Zero, "and must not page an operator about a file update-service keeps");
        Assert.That(report.Failed, Is.EqualTo(1));
        Assert.That(report.DeleteAttempts, Is.EqualTo(1));
        Assert.That(logs.Lines().Any(l => l.Contains("ORPHAN_RECLAIMED")), Is.False);
        Assert.That(logs.Lines().Any(l => l.StartsWith("Error", StringComparison.Ordinal)), Is.False,
            "the protected-file escalation is for files update-service keeps, not for a drift window");
        var warnings = logs.Lines().Where(l => l.StartsWith("Warning", StringComparison.Ordinal)).ToArray();
        Assert.That(warnings, Has.Length.EqualTo(1));
        Assert.That(warnings[0], Does.Contain(orphan).And.Contain("could not be verified"));
    }

    [Test]
    public async Task ReconciliationPass_AnInconclusiveProbe_IsWarnedAgainOnTheNextRun()
    {
        // Unlike a protected file, an unverified reclaim never enters the once-per-path warned set: it is a
        // transient drift window, so every run that meets it says so.
        using var logs = new LogCapture();
        const string orphan = "W3Champions/CustomGames/aged-99999999.w3x";
        const string agedIn = "W3Champions/CustomGames/aged-88888888.w3x";
        var handler = new ScriptedHttpHandler()
            .On(IsReclaimProbe, StillStored(agedIn))
            .On(HttpMethod.Get, Expired, HttpStatusCode.OK, Items())
            .On(HttpMethod.Get, Listing, HttpStatusCode.OK, Files(null, orphan))
            .On(HttpMethod.Get, ByPath, HttpStatusCode.NotFound)
            .On(HttpMethod.Delete, UsFile, HttpStatusCode.NoContent, "");
        var sweep = CreateSweep(handler, logs.CreateLogger<TemporaryMapExpirySweep>());

        await sweep.RunOnceAsync(Now, CancellationToken.None);
        await sweep.RunOnceAsync(Now, CancellationToken.None);

        Assert.That(logs.Lines().Count(l => l.StartsWith("Warning", StringComparison.Ordinal) && l.Contains(orphan)), Is.EqualTo(2));
    }
}
