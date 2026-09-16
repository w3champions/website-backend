using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;
using W3ChampionsStatisticService.Maps;
using W3ChampionsStatisticService.Sessions;

namespace WC3ChampionsStatisticService.Tests.Maps;

/// <summary>
/// Failure semantics of the new-map path (§6.3 steps 4, 7 and 9 with the segment B rulings): the H1 guard on an
/// update-service 409, a failure during the upload itself (never compensated), ambiguous matchmaking writes that
/// re-probe before deciding, client aborts, compensation retries, and the spool file's lifetime.
/// </summary>
[TestFixture]
public class TemporaryMapUploadServiceFailureTests : TemporaryMapUploadServiceTestBase
{
    private const string Conflict = "{\"message\":\"File already exists\"}";

    // ---- update-service 409 on a new map (ruling B3 as amended by H1) ------------------------

    [Test]
    public async Task UpdateService409_WithAKnownSha1_ReturnsThatRecordWithoutReuploading()
    {
        var handler = OnSequence(new ScriptedHttpHandler(), IsBySha1, Respond(HttpStatusCode.NotFound), Respond(HttpStatusCode.OK, Record(77)))
            .On(IsUsUpload, Respond(HttpStatusCode.Conflict, Conflict));
        var counts = new UploadCounts();

        var outcome = await Run(handler);

        Assert.That(outcome.Created, Is.False);
        Assert.That(outcome.Response.MapId, Is.EqualTo(77));
        Assert.That(handler.Requests.Select(Route), Is.EqualTo(new[] { "by-sha1", "us-upload", "by-sha1" }), "no retry, no delete");
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.Deduped);
    }

    [TestCase("deleted")]
    [TestCase("unknown")]
    public void UpdateService409_WithAKnownSha1WhoseFileIsNotPresent_Is502_WithNoDelete(string fileState)
    {
        var handler = OnSequence(new ScriptedHttpHandler(), IsBySha1,
                Respond(HttpStatusCode.NotFound), Respond(HttpStatusCode.OK, Record(77, fileState: fileState)))
            .On(IsUsUpload, Respond(HttpStatusCode.Conflict, Conflict));

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler));

        Assert.That(ex.Code, Is.EqualTo("UPSTREAM"));
        Assert.That(handler.Requests.Select(Route), Is.EqualTo(new[] { "by-sha1", "us-upload", "by-sha1" }));
    }

    [Test]
    public async Task UpdateService409_WithAnUnknownSha1_AndNoRecordAtThePath_DeletesTheStrayFileAndRetriesOnce()
    {
        var handler = UnknownSha1Handler()
            .On(IsByPath, Respond(HttpStatusCode.NotFound))
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""))
            .On(IsCreate, Respond(HttpStatusCode.Created, Record(5811)));
        OnSequence(handler, IsUsUpload, Respond(HttpStatusCode.Conflict, Conflict), Respond(HttpStatusCode.OK, UsUploadBody()));
        var counts = new UploadCounts();

        var outcome = await Run(handler);

        Assert.That(outcome.Created, Is.True);
        Assert.That(handler.Requests.Select(Route),
            Is.EqualTo(new[] { "by-sha1", "us-upload", "by-sha1", "by-path", "us-delete", "us-upload", "create" }));
        Assert.That(handler.Requests.Single(IsByPath).RequestUri!.Query, Is.EqualTo("?path=" + HttpUtility.UrlEncode(FileKey)));
        Assert.That(handler.Requests.Single(IsUsDelete).RequestUri!.Query, Is.EqualTo("?filePath=" + HttpUtility.UrlEncode(FileKey)));
        Assert.That(handler.RequestBodies.Where((_, i) => IsUsUpload(handler.Requests[i])).Last(), Does.Contain("\r\n\r\nabc\r\n"),
            "the retry streams the whole file again from a freshly opened spool stream");
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.Created);
    }

    [Test]
    public async Task AClientAbortDuringTheRetryAfterTheStrayDelete_StillStoresAndCreates()
    {
        // F-B: once the stray file is deleted, only the retried store can put bytes back at the fileKey.
        using var aborted = new CancellationTokenSource();
        var handler = UnknownSha1Handler()
            .On(IsByPath, Respond(HttpStatusCode.NotFound))
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""))
            .On(IsCreate, Respond(HttpStatusCode.Created, Record(5811)));
        OnSequence(handler, IsUsUpload, Respond(HttpStatusCode.Conflict, Conflict), _ =>
        {
            aborted.Cancel();
            return ScriptedHttpHandler.Json(HttpStatusCode.OK, UsUploadBody());
        });

        var outcome = await Run(handler, cancellationToken: aborted.Token);

        Assert.That(outcome.Created, Is.True);
        Assert.That(handler.Requests.Select(Route),
            Is.EqualTo(new[] { "by-sha1", "us-upload", "by-sha1", "by-path", "us-delete", "us-upload", "create" }));
    }

    [Test]
    public void UpdateService409_WhenARecordClaimsThePath_Is502_WithZeroDeletes_AndWarns()
    {
        // H1: the 8-hex suffix is grindable, so a colliding upload must never delete a live map's bytes.
        var handler = UnknownSha1Handler()
            .On(IsUsUpload, Respond(HttpStatusCode.Conflict, Conflict))
            .On(IsByPath, Respond(HttpStatusCode.OK, Record(4242, sha1: OtherSha1)))
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""));
        var counts = new UploadCounts();
        using var logs = new LogCapture();

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler, logger: logs.Logger));

        Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.Status502BadGateway));
        Assert.That(ex.Code, Is.EqualTo("UPSTREAM"));
        Assert.That(Count(handler, IsUsDelete), Is.Zero, "a file a record claims is never deleted");
        Assert.That(Count(handler, IsUsUpload), Is.EqualTo(1), "and never retried");
        Assert.That(Count(handler, IsCreate), Is.Zero);
        Assert.That(logs.Lines().Where(l => l.StartsWith("Warning") && l.Contains(Sha1) && l.Contains(OtherSha1) && l.Contains(FileKey)),
            Has.Exactly(1).Items, "one warning names both sha1s and the fileKey");
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.UpstreamError);
    }

    [TestCase("present")]
    [TestCase("deleted")]
    public void UpdateService409_WhenARecordInAnyStateClaimsThePath_NeverDeletes(string fileState)
    {
        var handler = UnknownSha1Handler()
            .On(IsUsUpload, Respond(HttpStatusCode.Conflict, Conflict))
            .On(IsByPath, Respond(HttpStatusCode.OK, Record(4242, fileState: fileState, sha1: OtherSha1)))
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""));

        Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler));

        Assert.That(Count(handler, IsUsDelete), Is.Zero);
    }

    private static readonly object[] FailingProbes =
    [
        new object[] { "500", Respond(HttpStatusCode.InternalServerError, "{}") },
        new object[] { "route-level 404", Respond(HttpStatusCode.NotFound, "<html>Cannot GET</html>") },
        new object[] { "200 without the record", Respond(HttpStatusCode.OK, "{}") },
        new object[] { "timeout", TimesOut() },
        new object[] { "transport", TransportFails() },
    ];

    [TestCaseSource(nameof(FailingProbes))]
    public void UpdateService409_WhenThePathProbeFails_Is502_WithNoDelete(string _, Func<HttpRequestMessage, HttpResponseMessage> byPath)
    {
        var handler = UnknownSha1Handler()
            .On(IsUsUpload, Respond(HttpStatusCode.Conflict, Conflict))
            .On(IsByPath, byPath)
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""));
        var counts = new UploadCounts();

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler));

        Assert.That(ex.Code, Is.EqualTo("UPSTREAM"));
        Assert.That(Count(handler, IsUsDelete), Is.Zero);
        Assert.That(Count(handler, IsUsUpload), Is.EqualTo(1));
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.UpstreamError);
    }

    [TestCaseSource(nameof(FailingProbes))]
    public void UpdateService409_WhenTheSha1ReprobeFails_Is502_WithNoDelete(string _, Func<HttpRequestMessage, HttpResponseMessage> reprobe)
    {
        var handler = OnSequence(new ScriptedHttpHandler(), IsBySha1, Respond(HttpStatusCode.NotFound), reprobe)
            .On(IsUsUpload, Respond(HttpStatusCode.Conflict, Conflict))
            .On(IsByPath, Respond(HttpStatusCode.NotFound))
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""));

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler));

        Assert.That(ex.Code, Is.EqualTo("UPSTREAM"));
        Assert.That(Count(handler, IsByPath), Is.Zero);
        Assert.That(Count(handler, IsUsDelete), Is.Zero);
    }

    [Test]
    public void UpdateService409OnTheRetryToo_Is502_AfterExactlyOneRetry()
    {
        var handler = UnknownSha1Handler()
            .On(IsUsUpload, Respond(HttpStatusCode.Conflict, Conflict))
            .On(IsByPath, Respond(HttpStatusCode.NotFound))
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""));

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler));

        Assert.That(ex.Code, Is.EqualTo("UPSTREAM"));
        Assert.That(Count(handler, IsUsUpload), Is.EqualTo(2));
        Assert.That(Count(handler, IsUsDelete), Is.EqualTo(1));
        Assert.That(Count(handler, IsCreate), Is.Zero);
    }

    [Test]
    public void UpdateService409_WhenTheStrayDeleteFails_Is502_WithoutRetryingTheUpload()
    {
        var handler = UnknownSha1Handler()
            .On(IsUsUpload, Respond(HttpStatusCode.Conflict, Conflict))
            .On(IsByPath, Respond(HttpStatusCode.NotFound))
            .On(IsUsDelete, Respond(HttpStatusCode.InternalServerError, "{}"));

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler));

        Assert.That(ex.Code, Is.EqualTo("UPSTREAM"));
        Assert.That(Count(handler, IsUsDelete), Is.EqualTo(1));
        Assert.That(Count(handler, IsUsUpload), Is.EqualTo(1));
    }

    // ---- A failure during the update-service upload itself -----------------------------------

    private static readonly object[] FailingUploads =
    [
        new object[] { "500", Respond(HttpStatusCode.InternalServerError, "{\"message\":\"disk on fire\"}") },
        new object[] { "413", Respond(HttpStatusCode.RequestEntityTooLarge, "") },
        new object[] { "200 not JSON", Respond(HttpStatusCode.OK, "<html>ok</html>") },
        new object[] { "timeout", TimesOut() },
        new object[] { "transport", TransportFails() },
    ];

    [TestCaseSource(nameof(FailingUploads))]
    public void AFailedUpload_Is502_AndIsNeverCompensated(string _, Func<HttpRequestMessage, HttpResponseMessage> upload)
    {
        // Whether the bytes landed is unknown, and a delete at the fileKey could remove a colliding live file.
        var handler = UnknownSha1Handler()
            .On(IsUsUpload, upload)
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""));
        var counts = new UploadCounts();

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler));

        Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.Status502BadGateway));
        Assert.That(ex.Code, Is.EqualTo("UPSTREAM"));
        Assert.That(handler.Requests.Select(Route), Is.EqualTo(new[] { "by-sha1", "us-upload" }));
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.UpstreamError);
    }

    [TestCase(false, TestName = "AClientAbortDuringTheUpload_PropagatesTheCancellation_WithoutCompensating_AndIsNotCounted")]
    [TestCase(true, TestName = "AClientAbortAsTheUploadIsAnswered_PropagatesTheCancellation_WithoutCompensating_AndIsNotCounted")]
    public void AClientAbortDuringTheUpload_PropagatesTheCancellation_WithoutCompensating_AndIsNotCounted(bool updateServiceAnswers)
    {
        using var aborted = new CancellationTokenSource();
        var handler = UnknownSha1Handler()
            .On(IsUsUpload, _ =>
            {
                aborted.Cancel();
                return updateServiceAnswers
                    ? ScriptedHttpHandler.Json(HttpStatusCode.OK, UsUploadBody())
                    : throw new OperationCanceledException(aborted.Token);
            })
            .On(IsCreate, Respond(HttpStatusCode.Created, Record(5811)))
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""));
        var counts = new UploadCounts();

        Assert.CatchAsync<OperationCanceledException>(() => Run(handler, cancellationToken: aborted.Token));

        Assert.That(handler.Requests.Select(Route), Is.EqualTo(new[] { "by-sha1", "us-upload" }));
        counts.AssertNothingCounted();
        AssertNoNewSpoolFiles([]);
    }

    [Test]
    public void AClientAbortBeforeAnyUpstreamCall_PropagatesTheCancellation()
    {
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();
        var handler = UnknownSha1Handler();
        var counts = new UploadCounts();

        Assert.CatchAsync<OperationCanceledException>(() => Run(handler, cancellationToken: aborted.Token));

        Assert.That(handler.Requests, Is.Empty);
        counts.AssertNothingCounted();
    }

    [Test]
    public void AClientAbortDuringTheDedupeProbe_PropagatesTheCancellation_BeforeAnyQuotaIsSpent()
    {
        using var aborted = new CancellationTokenSource();
        var limiter = new MintRateLimiter();
        var handler = new ScriptedHttpHandler()
            .On(IsBySha1, _ =>
            {
                aborted.Cancel();
                return ScriptedHttpHandler.Json(HttpStatusCode.NotFound, "{}");
            })
            .On(IsUsUpload, Respond(HttpStatusCode.OK, UsUploadBody()));
        var counts = new UploadCounts();

        Assert.CatchAsync<OperationCanceledException>(() => Run(handler, limiter, cancellationToken: aborted.Token));

        Assert.That(handler.Requests.Select(Route), Is.EqualTo(new[] { "by-sha1" }));
        Assert.That(limiter.Count, Is.Zero);
        counts.AssertNothingCounted();
    }

    [Test]
    public async Task AClientAbortBeforeAnUnansweredCreate_StillReprobesAndReturnsTheRecord()
    {
        using var aborted = new CancellationTokenSource();
        var handler = OnSequence(new ScriptedHttpHandler(), IsBySha1, Respond(HttpStatusCode.NotFound), Respond(HttpStatusCode.OK, Record(5811)))
            .On(IsUsUpload, Respond(HttpStatusCode.OK, UsUploadBody()))
            .On(IsCreate, _ =>
            {
                aborted.Cancel();
                throw new TaskCanceledException("simulated HttpClient timeout");
            });

        var outcome = await Run(handler, cancellationToken: aborted.Token);

        Assert.That(outcome.Response.MapId, Is.EqualTo(5811));
        Assert.That(handler.Requests.Select(Route), Is.EqualTo(new[] { "by-sha1", "us-upload", "create", "by-sha1" }));
    }

    [Test]
    public async Task AClientAbortAfterTheBytesAreStored_StillCompletesTheRecord()
    {
        using var aborted = new CancellationTokenSource();
        var handler = StoredNewMapHandler()
            .On(IsCreate, _ =>
            {
                aborted.Cancel();
                return ScriptedHttpHandler.Json(HttpStatusCode.Created, Record(5811));
            });

        var outcome = await Run(handler, cancellationToken: aborted.Token);

        Assert.That(outcome.Created, Is.True, "a disconnect after the bytes are stored must not leave a half state");
        Assert.That(Count(handler, IsUsDelete), Is.Zero);
    }

    [Test]
    public void AClientAbortWhileMatchmakingRefuses_StillCompensatesInOneAttempt()
    {
        using var aborted = new CancellationTokenSource();
        using var logs = new LogCapture();
        var handler = StoredNewMapHandler()
            .On(IsCreate, _ =>
            {
                aborted.Cancel();
                return ScriptedHttpHandler.Json(HttpStatusCode.InternalServerError, "{}");
            })
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""));

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler, logger: logs.Logger, cancellationToken: aborted.Token));

        Assert.That(ex.Code, Is.EqualTo("UPSTREAM"));
        Assert.That(Count(handler, IsUsDelete), Is.EqualTo(1));
        Assert.That(logs.Lines(), Has.None.Contains("ORPHAN"));
    }

    // ---- Ambiguous matchmaking create (no status, a broken success body, a 409 without its record) ----

    private static readonly object[] UnansweredCreates =
    [
        new object[] { "timeout", TimesOut() },
        new object[] { "transport", TransportFails() },
        new object[] { "201 not JSON", Respond(HttpStatusCode.Created, "<html>created</html>") },
        new object[] { "201 without the record", Respond(HttpStatusCode.Created, "{}") },
        new object[] { "409 without the record", Respond(HttpStatusCode.Conflict, "{}") },
    ];

    [TestCaseSource(nameof(UnansweredCreates))]
    public async Task AnUnansweredCreate_WhenTheReprobeFindsOurRecord_Is200Deduped_WithoutADelete(
        string _, Func<HttpRequestMessage, HttpResponseMessage> create)
    {
        var handler = OnSequence(new ScriptedHttpHandler(), IsBySha1, Respond(HttpStatusCode.NotFound), Respond(HttpStatusCode.OK, Record(5811)))
            .On(IsUsUpload, Respond(HttpStatusCode.OK, UsUploadBody()))
            .On(IsCreate, create)
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""));
        var counts = new UploadCounts();

        var outcome = await Run(handler);

        Assert.That(outcome.Created, Is.False);
        Assert.That(outcome.Response.MapId, Is.EqualTo(5811));
        Assert.That(outcome.Response.Path, Is.EqualTo(FileKey));
        Assert.That(handler.Requests.Select(Route), Is.EqualTo(new[] { "by-sha1", "us-upload", "create", "by-sha1" }));
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.Deduped);
    }

    [Test]
    public async Task AnUnansweredCreate_WhenTheReprobeFindsARecordAtAnotherPath_CompensatesOursAndReturnsIt()
    {
        const string olderPath = "W3Champions/CustomGames/older-a9993e36.w3x";
        var handler = OnSequence(new ScriptedHttpHandler(), IsBySha1,
                Respond(HttpStatusCode.NotFound), Respond(HttpStatusCode.OK, Record(99, olderPath)))
            .On(IsUsUpload, Respond(HttpStatusCode.OK, UsUploadBody()))
            .On(IsCreate, TimesOut())
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""));
        var counts = new UploadCounts();

        var outcome = await Run(handler);

        Assert.That(outcome.Response.MapId, Is.EqualTo(99));
        Assert.That(outcome.Response.Path, Is.EqualTo(olderPath));
        Assert.That(handler.Requests.Single(IsUsDelete).RequestUri!.Query, Is.EqualTo("?filePath=" + HttpUtility.UrlEncode(FileKey)));
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.Deduped);
    }

    [Test]
    public void AnUnansweredCreate_WhenTheReprobeFindsNothing_Compensates_AndIs502()
    {
        var handler = StoredNewMapHandler()
            .On(IsCreate, TimesOut())
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""));
        var counts = new UploadCounts();

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler));

        Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.Status502BadGateway));
        Assert.That(ex.Code, Is.EqualTo("UPSTREAM"));
        Assert.That(handler.Requests.Select(Route), Is.EqualTo(new[] { "by-sha1", "us-upload", "create", "by-sha1", "us-delete" }));
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.UpstreamError);
    }

    [TestCaseSource(nameof(FailingProbes))]
    public void AnUnansweredCreate_WhenTheReprobeFails_LeavesTheBytes_Warns_AndIs502(
        string _, Func<HttpRequestMessage, HttpResponseMessage> reprobe)
    {
        var handler = OnSequence(new ScriptedHttpHandler(), IsBySha1, Respond(HttpStatusCode.NotFound), reprobe)
            .On(IsUsUpload, Respond(HttpStatusCode.OK, UsUploadBody()))
            .On(IsCreate, TimesOut())
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""));
        var counts = new UploadCounts();
        using var logs = new LogCapture();

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler, logger: logs.Logger));

        Assert.That(ex.Code, Is.EqualTo("UPSTREAM"));
        Assert.That(Count(handler, IsUsDelete), Is.Zero, "with the outcome unknown, the bytes may back the record just created");
        Assert.That(logs.Lines().Where(l => l.StartsWith("Warning") && l.Contains(FileKey) && l.Contains("re-probe")), Is.Not.Empty);
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.UpstreamError);
    }

    [Test]
    public void AnUnexpectedFailureOfTheCreateCall_ReprobesBeforeCompensating_AndIs502()
    {
        // Neither an HTTP outcome nor an unanswered request (a local IO fault while the create runs): it still
        // follows a write attempt, so F-A asks matchmaking before deleting anything.
        var handler = StoredNewMapHandler()
            .On(IsCreate, _ => throw new IOException("simulated local fault"))
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""));
        var counts = new UploadCounts();

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler));

        Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.Status502BadGateway));
        Assert.That(ex.Code, Is.EqualTo("UPSTREAM"));
        Assert.That(handler.Requests.Select(Route), Is.EqualTo(new[] { "by-sha1", "us-upload", "create", "by-sha1", "us-delete" }));
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.UpstreamError);
    }

    // ---- The winning record must be a present temporary file (S-L2) ------------------------------

    private static readonly object[] UnusableWinners =
    [
        new object[] { "without a path", "{\"map\":{\"id\":99,\"name\":\"Legion TD\",\"temporary\":true,\"fileState\":\"present\"}}" },
        new object[] { "outside CustomGames", Record(99, "W3Champions/v10/Legion TD.w3x") },
        new object[] { "deleted at another path", Record(99, "W3Champions/CustomGames/older-a9993e36.w3x", fileState: "deleted") },
        new object[] { "deleted at our fileKey", Record(99, fileState: "deleted") },
        new object[] { "in an unknown state", Record(99, "W3Champions/CustomGames/older-a9993e36.w3x", fileState: "gone") },
        new object[] { "present inside the prefix but not a §6.4 fileKey (control character in the stem)", Record(99, "W3Champions/CustomGames/older\\u000aforged-a9993e36.w3x") },
        new object[] { "present inside the prefix without the sha1 suffix", Record(99, "W3Champions/CustomGames/older.w3x") },
    ];

    [TestCaseSource(nameof(UnusableWinners))]
    public void AConflictNamingAWinnerThatIsNotAPresentTemporaryFile_Is502_WithNoCompensation(string _, string winner)
    {
        var handler = StoredNewMapHandler()
            .On(IsCreate, Respond(HttpStatusCode.Conflict, winner))
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""));
        var counts = new UploadCounts();

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler));

        Assert.That(ex.Code, Is.EqualTo("UPSTREAM"));
        Assert.That(Count(handler, IsUsDelete), Is.Zero, "with the winner unusable the outcome is unknown, and the bytes may back it");
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.UpstreamError);
    }

    [TestCaseSource(nameof(UnusableWinners))]
    public void AFailedCreateWhoseReprobeFindsAWinnerThatIsNotAPresentTemporaryFile_Is502_WithNoCompensation(string _, string winner)
    {
        var handler = OnSequence(new ScriptedHttpHandler(), IsBySha1, Respond(HttpStatusCode.NotFound), Respond(HttpStatusCode.OK, winner))
            .On(IsUsUpload, Respond(HttpStatusCode.OK, UsUploadBody()))
            .On(IsCreate, TimesOut())
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""));

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler));

        Assert.That(ex.Code, Is.EqualTo("UPSTREAM"));
        Assert.That(Count(handler, IsUsDelete), Is.Zero);
    }

    [TestCase("W3Champions/v10/Legion TD.w3x", TestName = "a dedupe hit outside the temporary folder")]
    [TestCase("W3Champions/CustomGames/Legion\\u000aTD-a9993e36.w3x", TestName = "a dedupe hit inside the prefix with a control character in the stem")]
    [TestCase("W3Champions/CustomGames/Legion TD.w3x", TestName = "a dedupe hit inside the prefix without the sha1 suffix")]
    public void ADedupeHitAtAPathThatIsNotAFileKey_Is502_WithNothingStored(string path)
    {
        var handler = new ScriptedHttpHandler().On(IsBySha1, Respond(HttpStatusCode.OK, Record(5811, path)));

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler));

        Assert.That(ex.Code, Is.EqualTo("UPSTREAM"), "a 200 must never hand out a path the launcher cannot host");
        Assert.That(handler.Requests, Has.Count.EqualTo(1));
    }

    /// <summary>A matchmaking path whose stem carries CR LF and a fake log line; as JSON, with the escapes spliced in.</summary>
    private const string ForgingPath = "W3Champions/CustomGames/Legion TD\\u000d\\u000aforged line-a9993e36.w3x";

    [TestCase("dedupe hit outside the temporary folder")]
    [TestCase("dedupe hit outside the temporary folder, without a control character")]
    [TestCase("restore path that is not a fileKey")]
    [TestCase("winner after a failed create")]
    [TestCase("another record present after a failed file-restored")]
    public void AMatchmakingPathThatIsNotACleanTemporaryFilePath_IsLoggedAsInvalid(string site)
    {
        // S2-2: the file sink renders strings raw, so a drifted or forged matchmaking path must never reach a log line as sent;
        // only a temporary map file path without a control character is rendered, anything else as the placeholder.
        var handler = site switch
        {
            "dedupe hit outside the temporary folder" => new ScriptedHttpHandler()
                .On(IsBySha1, Respond(HttpStatusCode.OK, Record(5811, ForgingPath.Replace("CustomGames", "v10")))),
            "dedupe hit outside the temporary folder, without a control character" => new ScriptedHttpHandler()
                .On(IsBySha1, Respond(HttpStatusCode.OK, Record(5811, "W3Champions/v10/forged-a9993e36.w3x"))),
            "restore path that is not a fileKey" => DeletedRecordHandler()
                .On(IsVerifyProof, Respond(HttpStatusCode.OK, Verified(5811, ForgingPath))),
            "winner after a failed create" => OnSequence(new ScriptedHttpHandler(), IsBySha1,
                    Respond(HttpStatusCode.NotFound), Respond(HttpStatusCode.OK, Record(99, ForgingPath, fileState: "deleted")))
                .On(IsUsUpload, Respond(HttpStatusCode.OK, UsUploadBody()))
                .On(IsCreate, Respond(HttpStatusCode.BadRequest, "{}")),
            _ => OnSequence(new ScriptedHttpHandler(), IsBySha1,
                    Respond(HttpStatusCode.OK, Record(5811, fileState: "deleted")), Respond(HttpStatusCode.OK, Record(7777, ForgingPath)))
                .On(IsVerifyProof, Respond(HttpStatusCode.OK, Verified(5811)))
                .On(IsUsUpload, Respond(HttpStatusCode.OK, UsUploadBody()))
                .On(IsFileRestored, Respond(HttpStatusCode.InternalServerError, "{}")),
        };
        handler.On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""));
        using var logs = new LogCapture();

        Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler, logger: logs.Logger));

        Assert.That(logs.Lines().Any(l => l.Contains("forged", StringComparison.Ordinal)), Is.False, "the path was rendered as sent");
        Assert.That(logs.Lines().Count(l => l.Contains("=\"invalid\"", StringComparison.Ordinal)), Is.EqualTo(1),
            "the placeholder stands in for the path, as the property value too");
        Assert.That(Count(handler, IsUsDelete), Is.Zero);
    }

    // ---- Compensation ---------------------------------------------------------------------------

    [Test]
    public void ACompensationThatKeepsFailing_TriesFourTimes_ThenLogsAnOrphan()
    {
        var handler = StoredNewMapHandler()
            .On(IsCreate, Respond(HttpStatusCode.InternalServerError, "{}"))
            .On(IsUsDelete, Respond(HttpStatusCode.ServiceUnavailable, ""));
        using var logs = new LogCapture();

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler, logger: logs.Logger));

        Assert.That(ex.Code, Is.EqualTo("UPSTREAM"), "the original failure is answered, not the compensation's");
        Assert.That(Count(handler, IsUsDelete), Is.EqualTo(4), "one attempt and three retries");
        Assert.That(CompensationWaits, Is.EqualTo(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4) }));
        Assert.That(logs.Lines().Where(l => l.StartsWith("Warning") && l.Contains("ORPHAN") && l.Contains(FileKey)), Has.Exactly(1).Items);
    }

    [Test]
    public void ACompensationThatSucceedsOnARetry_StopsRetrying()
    {
        var handler = StoredNewMapHandler().On(IsCreate, Respond(HttpStatusCode.InternalServerError, "{}"));
        OnSequence(handler, IsUsDelete, Respond(HttpStatusCode.ServiceUnavailable, ""), TransportFails(), Respond(HttpStatusCode.NoContent, ""));
        using var logs = new LogCapture();

        Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler, logger: logs.Logger));

        Assert.That(Count(handler, IsUsDelete), Is.EqualTo(3));
        Assert.That(CompensationWaits, Is.EqualTo(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2) }));
        Assert.That(logs.Lines(), Has.None.Contains("ORPHAN"));
    }

    [Test]
    public void ProductionDefaults_SpoolIntoTheTempUploadDir_AndReallyWaitBetweenRetries()
    {
        var service = new TemporaryMapUploadService(null, null, new MintRateLimiter(), FileKeyLock,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TemporaryMapUploadService>.Instance);

        Assert.That(service.SpoolDirectory, Is.Null);
        Assert.That(service.EffectiveSpoolDirectory, Is.EqualTo(TemporaryMapLimits.TempUploadDir), "production spools into the shared upload dir");
        Assert.That(service.WaitAsync(TimeSpan.FromMinutes(1)).IsCompleted, Is.False, "the default wait is a real delay");
    }

    [Test]
    public void TheSpoolDirectorySeam_WinsOverTheProductionDefault()
    {
        Assert.That(CreateService(new ScriptedHttpHandler()).EffectiveSpoolDirectory, Is.EqualTo(SpoolDirectory));
    }

    // ---- The spool file and the body -----------------------------------------------------------

    [Test]
    public async Task TheSpoolFileExistsWhileTheBytesAreStreamed_AndNoSpoolFileSurvivesAnyOutcome()
    {
        var before = FilesIn(SpoolDirectory);
        var filesDuringUpload = Array.Empty<string>();
        var ok = UnknownSha1Handler()
            .On(IsUsUpload, _ =>
            {
                filesDuringUpload = FilesIn(SpoolDirectory);
                return ScriptedHttpHandler.Json(HttpStatusCode.OK, UsUploadBody());
            })
            .On(IsCreate, Respond(HttpStatusCode.Created, Record(1)));
        await Run(ok);
        Assert.That(filesDuringUpload, Has.Length.EqualTo(1), "the upload streams from a spool file in the configured directory");

        Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(new ScriptedHttpHandler(), metadataSha1: OtherSha1));
        Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(StoredNewMapHandler()
            .On(IsCreate, Respond(HttpStatusCode.InternalServerError, "{}"))
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""))));
        Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(UnknownSha1Handler().On(IsUsUpload, TimesOut())));

        AssertNoNewSpoolFiles(before);
    }

    [Test]
    public void ASpoolFault_EscapesUnchanged_AndIsCountedOnce()
    {
        // The spool directory cannot be created underneath a regular file.
        Directory.CreateDirectory(TestRoot);
        var blocker = Path.Combine(TestRoot, "not-a-directory");
        File.WriteAllText(blocker, "");
        var blockedService = new TemporaryMapUploadService(null, null, new MintRateLimiter(), FileKeyLock,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TemporaryMapUploadService>.Instance)
        {
            SpoolDirectory = Path.Combine(blocker, "spool"),
        };
        var (body, contentType) = BuildMultipart(Metadata(), "abc"u8.ToArray());
        var counts = new UploadCounts();

        Assert.ThrowsAsync<TemporaryMapSpoolException>(() => blockedService.HandleUploadAsync(body, contentType, BattleTag, default));

        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.ServerError);
        Assert.That(TemporaryMapMetrics.Results.ServerError, Is.EqualTo("server_error"));
    }

    [Test]
    public void ATruncatedBody_EscapesAsTheBodyError_AndIsCountedAsRejected()
    {
        var (bytes, contentType) = BuildMultipartBytes(Metadata(), "abc"u8.ToArray());
        var truncated = new MemoryStream(bytes, 0, bytes.Length - 20);
        var handler = new ScriptedHttpHandler();
        var counts = new UploadCounts();

        Assert.CatchAsync<IOException>(() => CreateService(handler).HandleUploadAsync(truncated, contentType, BattleTag, default));

        Assert.That(handler.Requests, Is.Empty);
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.Rejected);
        AssertNoNewSpoolFiles([]);
    }

    [Test]
    public void ABodyErrorAfterTheClientAborted_IsNotCounted()
    {
        using var aborted = new CancellationTokenSource();
        var (bytes, contentType) = BuildMultipartBytes(Metadata(), "abc"u8.ToArray());
        var body = new AbortingStream(new MemoryStream(bytes, 0, bytes.Length - 20), aborted);
        var counts = new UploadCounts();

        Assert.CatchAsync<IOException>(() => CreateService(new ScriptedHttpHandler()).HandleUploadAsync(body, contentType, BattleTag, aborted.Token));

        counts.AssertNothingCounted();
    }

    /// <summary>A body that, like Kestrel on a reset connection, cancels the request token before its read fails.</summary>
    private sealed class AbortingStream(Stream inner, CancellationTokenSource aborted) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) => Fail(inner.Read(buffer, offset, count));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => Fail(await inner.ReadAsync(buffer, CancellationToken.None));

        private int Fail(int read)
        {
            if (read > 0)
            {
                return read;
            }

            aborted.Cancel();
            throw new IOException("simulated connection reset");
        }
    }
}
