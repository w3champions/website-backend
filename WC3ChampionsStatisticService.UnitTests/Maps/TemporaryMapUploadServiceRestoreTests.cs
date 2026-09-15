using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using W3ChampionsStatisticService.Maps;
using W3ChampionsStatisticService.Sessions;

namespace WC3ChampionsStatisticService.Tests.Maps;

/// <summary>
/// §3.6 / §6.3 owner re-host after expiry: verify the proof without mutating, store the bytes at the record's own path
/// (with the H1 guard on an update-service 409), check the digests, and only then flip fileState to present. The
/// capture is never validated here (ruling B4). An unanswered file-restored call re-probes matchmaking before deciding.
/// </summary>
[TestFixture]
public class TemporaryMapUploadServiceRestoreTests : TemporaryMapUploadServiceTestBase
{
    private const int MapId = 5811;
    private const string RestoredRecord = "{\"map\":{\"id\":5811,\"name\":\"Legion TD\",\"path\":\"" + FileKey + "\",\"fileState\":\"present\"}}";
    private const string DeletedRecordBody = "{\"map\":{\"id\":5811,\"name\":\"Legion TD\",\"path\":\"" + FileKey + "\",\"fileState\":\"deleted\"}}";

    /// <summary>verify-proof names record 5811 as deleted and update-service stores the bytes.</summary>
    private static ScriptedHttpHandler VerifiedAndStored()
        => DeletedRecordHandler()
            .On(IsVerifyProof, Respond(HttpStatusCode.OK, Verified(MapId)))
            .On(IsUsUpload, Respond(HttpStatusCode.OK, UsUploadBody()));

    [Test]
    public async Task ExpiredRecord_VerifiesProof_Uploads_ThenFlipsFileStateToPresent()
    {
        var handler = VerifiedAndStored().On(IsFileRestored, Respond(HttpStatusCode.OK, RestoredRecord));
        var limiter = new MintRateLimiter();
        var counts = new UploadCounts();

        var outcome = await Run(handler, limiter);

        Assert.That(outcome.Created, Is.False);
        Assert.That(outcome.Response.MapId, Is.EqualTo(MapId));
        Assert.That(outcome.Response.Path, Is.EqualTo(FileKey), "the record's stored path wins over the uploader's name");
        Assert.That(outcome.Response.Name, Is.EqualTo("Legion TD"));
        Assert.That(outcome.Response.Sha1, Is.EqualTo(Sha1));
        Assert.That(limiter.Count, Is.Zero, "a restore does not consume upload quota");
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.Restored);

        Assert.That(handler.Requests.Select(Route), Is.EqualTo(new[] { "by-sha1", "verify-proof", "us-upload", "file-restored" }),
            "verify before mutate, and file-restored only AFTER the bytes are stored");
        Assert.That(JObject.Parse(handler.RequestBodies[1])["proofHash"]!.Value<string>(), Is.EqualTo(ProofHash));
        Assert.That(handler.RequestBodies[2], Does.Match("name=\"?mapId\"?\r\n(?:[^\r\n]+\r\n)*\r\n5811\r\n"), "stored under the record's id");
        Assert.That(handler.RequestBodies[2], Does.Contain(UsFileName));
        Assert.That(handler.Requests[3].RequestUri!.AbsolutePath, Does.EndWith("/maps/temporary/5811/file-restored"));
        var restoredBody = JObject.Parse(handler.RequestBodies[3]);
        Assert.That(restoredBody["mapProof"]!.Value<string>(), Is.EqualTo(MapProofValue));
        Assert.That(restoredBody["sha1"]!.Value<string>(), Is.EqualTo(Sha1));
        Assert.That(restoredBody["uploader"]!.Value<string>(), Is.EqualTo(BattleTag));
    }

    [Test]
    public async Task AClientAbortAfterTheBytesAreStored_StillFlipsTheRecordToPresent()
    {
        using var aborted = new CancellationTokenSource();
        var handler = VerifiedAndStored().On(IsFileRestored, _ =>
        {
            aborted.Cancel();
            return ScriptedHttpHandler.Json(HttpStatusCode.OK, RestoredRecord);
        });

        var outcome = await Run(handler, cancellationToken: aborted.Token);

        Assert.That(outcome.Response.MapId, Is.EqualTo(MapId));
        Assert.That(Count(handler, IsUsDelete), Is.Zero, "a disconnect after the bytes are stored must not leave a half state");
    }

    [Test]
    public async Task ExpiredRecord_WithoutACapture_IsRestored()
    {
        // The launcher omits capture after an "expired" pre-check (Appendix A.3), so a restore must not require one.
        var handler = VerifiedAndStored().On(IsFileRestored, Respond(HttpStatusCode.OK, RestoredRecord));

        var outcome = await Run(handler, withCapture: false);

        Assert.That(outcome.Response.MapId, Is.EqualTo(MapId));
        Assert.That(Count(handler, IsFileRestored), Is.EqualTo(1));
        Assert.That(Count(handler, IsUsDelete), Is.Zero);
    }

    [Test]
    public async Task ExpiredRecord_IgnoresASuppliedCapture_EvenAnImpossibleOne()
    {
        var handler = VerifiedAndStored().On(IsFileRestored, Respond(HttpStatusCode.OK, RestoredRecord));

        var outcome = await Run(handler, capture: CaptureJson(slotCount: 99, maxTeams: 0));

        Assert.That(outcome.Response.MapId, Is.EqualTo(MapId), "the record's layout is authoritative on a restore");
        Assert.That(Count(handler, IsUsDelete), Is.Zero);
    }

    [Test]
    public void ExpiredRecord_WithAnUnknownProof_AbortsBeforeTouchingUpdateService()
    {
        var handler = DeletedRecordHandler().On(IsVerifyProof, Respond(HttpStatusCode.NotFound));
        var counts = new UploadCounts();

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler));

        Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
        Assert.That(ex.Code, Is.EqualTo("PROOF_MISMATCH"));
        Assert.That(Count(handler, IsUsUpload), Is.Zero);
        Assert.That(Count(handler, IsFileRestored), Is.Zero);
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.Rejected);
    }

    [Test]
    public void VerifyProofNamingADifferentRecord_Is500_TempMapKeyMismatch()
    {
        var handler = DeletedRecordHandler()
            .On(IsVerifyProof, Respond(HttpStatusCode.OK, Verified(42, "W3Champions/CustomGames/other-00000000.w3x")));
        var counts = new UploadCounts();

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler));

        Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.Status500InternalServerError));
        Assert.That(ex.Code, Is.EqualTo("TEMP_MAP_KEY_MISMATCH"));
        Assert.That(Count(handler, IsUsUpload), Is.Zero);
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.UpstreamError);
    }

    [Test]
    public async Task VerifyProofNamingTheSameRecordAsPresent_IsADedupeHit_WithNoWrite()
    {
        // A concurrent restore won between the dedupe probe and verify-proof (ruling S4 / Deviation 11).
        var handler = DeletedRecordHandler().On(IsVerifyProof, Respond(HttpStatusCode.OK, Verified(MapId, fileState: "present")));
        var counts = new UploadCounts();

        var outcome = await Run(handler);

        Assert.That(outcome.Created, Is.False);
        Assert.That(outcome.Response.MapId, Is.EqualTo(MapId));
        Assert.That(outcome.Response.Path, Is.EqualTo(FileKey));
        Assert.That(Count(handler, IsUsUpload), Is.Zero);
        Assert.That(Count(handler, IsFileRestored), Is.Zero);
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.Deduped);
    }

    [TestCase("")]
    [TestCase("expired")]
    public void VerifyProofWithAnUnknownFileState_Is502_WithNoWrite(string fileState)
    {
        var handler = DeletedRecordHandler().On(IsVerifyProof, Respond(HttpStatusCode.OK, Verified(MapId, fileState: fileState)));

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler));

        Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.Status502BadGateway));
        Assert.That(ex.Code, Is.EqualTo("UPSTREAM"));
        Assert.That(Count(handler, IsUsUpload), Is.Zero);
    }

    [TestCase("W3Champions/v10/EchoIsles.w3x")]
    [TestCase("W3Champions/CustomGames/../v10/EchoIsles.w3x")]
    [TestCase("W3Champions/CustomGames/")]
    [TestCase("")]
    [TestCase("W3Champions/CustomGames/sub/Legion TD-a9993e36.w3x", TestName = "a stored path with a second segment (S-I2)")]
    [TestCase("W3Champions/CustomGames/Legion TD-a9993e36.zip", TestName = "a stored path without a map extension (S-I2)")]
    [TestCase("W3Champions/CustomGames/Legion TD-a9993e36.w3x\\u000a", TestName = "a stored path with a control character (S-I2)")]
    public void AStoredRecordPathThatIsNotATemporaryFile_Is500_TempMapKeyMismatch_WithNoWrite(string storedPath)
    {
        var handler = DeletedRecordHandler().On(IsVerifyProof, Respond(HttpStatusCode.OK, Verified(MapId, storedPath)));

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler));

        Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.Status500InternalServerError));
        Assert.That(ex.Code, Is.EqualTo("TEMP_MAP_KEY_MISMATCH"));
        Assert.That(handler.Requests.Select(Route), Is.EqualTo(new[] { "by-sha1", "verify-proof" }));
    }

    [Test]
    public void VerifyProofFailing_Is502_WithNoWrite(
        [Values(HttpStatusCode.InternalServerError, HttpStatusCode.NotFound)] HttpStatusCode status)
    {
        // A 404 without the empty-object body is a route-level miss, never "unknown proof".
        var handler = DeletedRecordHandler().On(IsVerifyProof, Respond(status, "<html>Cannot POST</html>"));
        var counts = new UploadCounts();

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler));

        Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.Status502BadGateway));
        Assert.That(ex.Code, Is.EqualTo("UPSTREAM"));
        Assert.That(Count(handler, IsUsUpload), Is.Zero);
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.UpstreamError);
    }

    [Test]
    public void UpdateServiceFailingAfterVerifyProof_LeavesTheRecordDeleted()
    {
        var handler = DeletedRecordHandler()
            .On(IsVerifyProof, Respond(HttpStatusCode.OK, Verified(MapId)))
            .On(IsUsUpload, Respond(HttpStatusCode.InternalServerError, "{\"message\":\"disk on fire\"}"));

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler));

        Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.Status502BadGateway));
        Assert.That(ex.Code, Is.EqualTo("UPSTREAM"));
        Assert.That(Count(handler, IsFileRestored), Is.Zero, "fileState must never become present without bytes behind it");
        Assert.That(Count(handler, IsUsDelete), Is.Zero, "a failed upload is never compensated");
    }

    [Test]
    public void ARestoredFileWithADifferentDigest_Is502_ParserMismatch_AndCompensates()
    {
        var handler = DeletedRecordHandler()
            .On(IsVerifyProof, Respond(HttpStatusCode.OK, Verified(MapId)))
            .On(IsUsUpload, Respond(HttpStatusCode.OK, UsUploadBody(mapProofHash: new string('0', 64))))
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""));

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler, withCapture: false));

        Assert.That(ex.Code, Is.EqualTo("PARSER_MISMATCH"));
        Assert.That(Count(handler, IsUsDelete), Is.EqualTo(1));
        Assert.That(Count(handler, IsFileRestored), Is.Zero);
    }

    private static readonly object[] FailedFileRestored =
    [
        new object[] { "timeout", TimesOut() },
        new object[] { "transport", TransportFails() },
        new object[] { "200 not JSON", Respond(HttpStatusCode.OK, "<html>ok</html>") },
        new object[] { "200 without the record", Respond(HttpStatusCode.OK, "{}") },
        new object[] { "409 (sha1 or proof differ)", Respond(HttpStatusCode.Conflict, "{\"errors\":[{\"param\":\"mapProof\",\"msg\":\"nope\"}]}") },
        new object[] { "400", Respond(HttpStatusCode.BadRequest, "{\"errors\":[{\"param\":\"mapProof\",\"msg\":\"nope\"}]}") },
        new object[] { "500", Respond(HttpStatusCode.InternalServerError, "{}") },
    ];

    [TestCaseSource(nameof(FailedFileRestored))]
    public async Task FileRestoredFailing_WhenTheReprobeFindsItPresent_Is200Restored_WithoutADelete(
        string _, System.Func<HttpRequestMessage, HttpResponseMessage> fileRestored)
    {
        // F-A: a refusal is re-probed too, because matchmaking or a proxy can answer non-2xx after the write committed.
        var handler = OnSequence(new ScriptedHttpHandler(), IsBySha1,
                Respond(HttpStatusCode.OK, Record(MapId, fileState: "deleted")),
                Respond(HttpStatusCode.OK, Record(MapId, fileState: "present")))
            .On(IsVerifyProof, Respond(HttpStatusCode.OK, Verified(MapId)))
            .On(IsUsUpload, Respond(HttpStatusCode.OK, UsUploadBody()))
            .On(IsFileRestored, fileRestored);
        var counts = new UploadCounts();

        var outcome = await Run(handler);

        Assert.That(outcome.Response.MapId, Is.EqualTo(MapId));
        Assert.That(outcome.Response.Path, Is.EqualTo(FileKey));
        Assert.That(Count(handler, IsBySha1), Is.EqualTo(2), "exactly one re-probe");
        Assert.That(Count(handler, IsUsDelete), Is.Zero);
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.Restored);
    }

    [Test]
    public void FileRestoredFailing_WhenTheReprobeDoesNotFindItPresent_Compensates_AndIs502(
        [Values("timeout", "409", "500")] string fileRestored, [Values("still deleted", "gone")] string reprobe)
    {
        var handler = OnSequence(new ScriptedHttpHandler(), IsBySha1,
                Respond(HttpStatusCode.OK, Record(MapId, fileState: "deleted")),
                reprobe == "gone" ? Respond(HttpStatusCode.NotFound) : Respond(HttpStatusCode.OK, Record(MapId, fileState: "deleted")))
            .On(IsVerifyProof, Respond(HttpStatusCode.OK, Verified(MapId)))
            .On(IsUsUpload, Respond(HttpStatusCode.OK, UsUploadBody()))
            .On(IsFileRestored, fileRestored switch
            {
                "timeout" => TimesOut(),
                "409" => Respond(HttpStatusCode.Conflict, "{\"errors\":[{\"param\":\"mapProof\",\"msg\":\"nope\"}]}"),
                _ => Respond(HttpStatusCode.InternalServerError, "{}"),
            })
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""));
        var counts = new UploadCounts();

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler));

        Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.Status502BadGateway));
        Assert.That(ex.Code, Is.EqualTo("UPSTREAM"));
        Assert.That(handler.Requests.Select(Route),
            Is.EqualTo(new[] { "by-sha1", "verify-proof", "us-upload", "file-restored", "by-sha1", "us-delete" }),
            "the stored bytes are rolled back only after the re-probe, so a later re-upload can retry cleanly");
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.UpstreamError);
    }

    private static readonly object[] FailingReprobes =
    [
        new object[] { "500", Respond(HttpStatusCode.InternalServerError, "{}") },
        new object[] { "timeout", TimesOut() },
        new object[] { "route-level 404", Respond(HttpStatusCode.NotFound, "Cannot GET") },
        new object[] { "unknown fileState", Respond(HttpStatusCode.OK, Record(MapId, fileState: "gone")) },
    ];

    [TestCaseSource(nameof(FailingReprobes))]
    public void FileRestoredTimingOut_WhenTheReprobeFails_LeavesTheBytes_AndIs502(
        string _, System.Func<HttpRequestMessage, HttpResponseMessage> reprobe)
    {
        var handler = OnSequence(new ScriptedHttpHandler(), IsBySha1, Respond(HttpStatusCode.OK, Record(MapId, fileState: "deleted")), reprobe)
            .On(IsVerifyProof, Respond(HttpStatusCode.OK, Verified(MapId)))
            .On(IsUsUpload, Respond(HttpStatusCode.OK, UsUploadBody()))
            .On(IsFileRestored, TimesOut())
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""));
        var counts = new UploadCounts();

        using var logs = new LogCapture();

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler, logger: logs.Logger));

        Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.Status502BadGateway));
        Assert.That(ex.Code, Is.EqualTo("UPSTREAM"));
        Assert.That(Count(handler, IsUsDelete), Is.Zero, "with the outcome unknown, the bytes may back a present record");
        Assert.That(logs.Lines(), Has.None.Contains("reconciliation sweep"), "the record claims this path, so the sweep never reclaims it");
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.UpstreamError);
    }

    [Test]
    public void FileRestoredTimingOut_WhenTheReprobeFails_WarnsThatALaterRestoreReplacesTheBytes()
    {
        var handler = OnSequence(new ScriptedHttpHandler(), IsBySha1,
                Respond(HttpStatusCode.OK, Record(MapId, fileState: "deleted")), Respond(HttpStatusCode.InternalServerError, "{}"))
            .On(IsVerifyProof, Respond(HttpStatusCode.OK, Verified(MapId)))
            .On(IsUsUpload, Respond(HttpStatusCode.OK, UsUploadBody()))
            .On(IsFileRestored, TimesOut());
        using var logs = new LogCapture();

        Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler, logger: logs.Logger));

        Assert.That(logs.Lines().Where(l => l.StartsWith("Warning") && l.Contains("re-probe") && l.Contains(FileKey)
                                            && l.Contains("5811") && l.Contains("a later restore replaces them")),
            Has.Exactly(1).Items, "R-Minor2: on a restore the record claims the path, so only a later restore reclaims the bytes");
    }

    [TestCase("{}", HttpStatusCode.NotFound, TestName = "no record claims the path")]
    [TestCase(DeletedRecordBody, HttpStatusCode.OK, TestName = "the record being restored claims the path and is still deleted")]
    public async Task UpdateService409_WhenNoOtherRecordClaimsThePath_DeletesTheStrayFileAndRetriesOnce(string byPathBody, HttpStatusCode byPathStatus)
    {
        var handler = DeletedRecordHandler()
            .On(IsVerifyProof, Respond(HttpStatusCode.OK, Verified(MapId)))
            .On(IsByPath, Respond(byPathStatus, byPathBody))
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""))
            .On(IsFileRestored, Respond(HttpStatusCode.OK, RestoredRecord));
        OnSequence(handler, IsUsUpload,
            Respond(HttpStatusCode.Conflict, "{\"message\":\"File already exists\"}"),
            Respond(HttpStatusCode.OK, UsUploadBody()));

        var outcome = await Run(handler);

        Assert.That(outcome.Response.MapId, Is.EqualTo(MapId));
        Assert.That(handler.Requests.Select(Route),
            Is.EqualTo(new[] { "by-sha1", "verify-proof", "us-upload", "by-path", "us-delete", "us-upload", "file-restored" }));
        Assert.That(handler.Requests.Single(IsByPath).RequestUri!.Query, Is.EqualTo("?path=" + HttpUtility.UrlEncode(FileKey)));
    }

    [Test]
    public async Task UpdateService409_WhenTheRecordBeingRestoredIsAlreadyPresentAtThePath_IsADedupeHit_WithZeroDeletes()
    {
        // S-M1 mirrors S4: a concurrent restore of this record stored the bytes and flipped it first.
        var handler = DeletedRecordHandler()
            .On(IsVerifyProof, Respond(HttpStatusCode.OK, Verified(MapId)))
            .On(IsUsUpload, Respond(HttpStatusCode.Conflict, "{\"message\":\"File already exists\"}"))
            .On(IsByPath, Respond(HttpStatusCode.OK, RestoredRecord))
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""));
        var counts = new UploadCounts();

        var outcome = await Run(handler);

        Assert.That(outcome.Created, Is.False);
        Assert.That(outcome.Response.MapId, Is.EqualTo(MapId));
        Assert.That(outcome.Response.Path, Is.EqualTo(FileKey));
        Assert.That(handler.Requests.Select(Route), Is.EqualTo(new[] { "by-sha1", "verify-proof", "us-upload", "by-path" }),
            "no delete, no retried store and no file-restored");
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.Deduped);
    }

    [TestCase("gone")]
    [TestCase("")]
    public void UpdateService409_WhenTheRecordBeingRestoredClaimsThePathInAnUnknownState_Is502_WithNoDelete(string fileState)
    {
        var handler = DeletedRecordHandler()
            .On(IsVerifyProof, Respond(HttpStatusCode.OK, Verified(MapId)))
            .On(IsUsUpload, Respond(HttpStatusCode.Conflict, "{\"message\":\"File already exists\"}"))
            .On(IsByPath, Respond(HttpStatusCode.OK, Record(MapId, fileState: fileState)))
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""));

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler));

        Assert.That(ex.Code, Is.EqualTo("UPSTREAM"));
        Assert.That(handler.Requests.Select(Route), Is.EqualTo(new[] { "by-sha1", "verify-proof", "us-upload", "by-path" }));
    }

    [Test]
    public void UpdateService409_WhenADifferentRecordClaimsThePath_Is502_WithZeroDeletes_AndWarns()
    {
        var handler = DeletedRecordHandler()
            .On(IsVerifyProof, Respond(HttpStatusCode.OK, Verified(MapId)))
            .On(IsUsUpload, Respond(HttpStatusCode.Conflict, "{\"message\":\"File already exists\"}"))
            .On(IsByPath, Respond(HttpStatusCode.OK, Record(7777, sha1: OtherSha1)))
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""));
        using var logs = new LogCapture();

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler, logger: logs.Logger));

        Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.Status502BadGateway));
        Assert.That(ex.Code, Is.EqualTo("UPSTREAM"));
        Assert.That(Count(handler, IsUsDelete), Is.Zero, "a file another record claims is never deleted");
        Assert.That(Count(handler, IsUsUpload), Is.EqualTo(1), "and never retried");
        Assert.That(Count(handler, IsFileRestored), Is.Zero);
        Assert.That(logs.Lines().Where(l => l.StartsWith("Warning") && l.Contains("5811") && l.Contains("7777") && l.Contains(FileKey)),
            Has.Exactly(1).Items, "one warning names both map ids and the path");
    }

    private static readonly object[] FailingPathProbes =
    [
        new object[] { "500", Respond(HttpStatusCode.InternalServerError, "{}") },
        new object[] { "route-level 404", Respond(HttpStatusCode.NotFound, "Cannot GET /maps/temporary/by-path") },
        new object[] { "timeout", TimesOut() },
    ];

    [TestCaseSource(nameof(FailingPathProbes))]
    public void UpdateService409_WhenThePathProbeFails_Is502_WithNoDelete(string _, System.Func<HttpRequestMessage, HttpResponseMessage> byPath)
    {
        var handler = DeletedRecordHandler()
            .On(IsVerifyProof, Respond(HttpStatusCode.OK, Verified(MapId)))
            .On(IsUsUpload, Respond(HttpStatusCode.Conflict, "{\"message\":\"File already exists\"}"))
            .On(IsByPath, byPath)
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""));

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler));

        Assert.That(ex.Code, Is.EqualTo("UPSTREAM"));
        Assert.That(Count(handler, IsUsDelete), Is.Zero);
        Assert.That(Count(handler, IsUsUpload), Is.EqualTo(1));
    }

    [Test]
    public void UpdateService409OnTheRetryToo_Is502_AfterExactlyOneRetry()
    {
        var handler = DeletedRecordHandler()
            .On(IsVerifyProof, Respond(HttpStatusCode.OK, Verified(MapId)))
            .On(IsUsUpload, Respond(HttpStatusCode.Conflict, "{\"message\":\"File already exists\"}"))
            .On(IsByPath, Respond(HttpStatusCode.NotFound))
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""));

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler));

        Assert.That(ex.Code, Is.EqualTo("UPSTREAM"));
        Assert.That(Count(handler, IsUsUpload), Is.EqualTo(2));
        Assert.That(Count(handler, IsUsDelete), Is.EqualTo(1));
        Assert.That(Count(handler, IsFileRestored), Is.Zero);
    }

    [Test]
    public void UpdateService409_WhenTheStrayDeleteFails_Is502_WithoutRetryingTheUpload()
    {
        var handler = DeletedRecordHandler()
            .On(IsVerifyProof, Respond(HttpStatusCode.OK, Verified(MapId)))
            .On(IsUsUpload, Respond(HttpStatusCode.Conflict, "{\"message\":\"File already exists\"}"))
            .On(IsByPath, Respond(HttpStatusCode.NotFound))
            .On(IsUsDelete, Respond(HttpStatusCode.InternalServerError, "{}"));

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler));

        Assert.That(ex.Code, Is.EqualTo("UPSTREAM"));
        Assert.That(Count(handler, IsUsDelete), Is.EqualTo(1));
        Assert.That(Count(handler, IsUsUpload), Is.EqualTo(1));
    }
}
