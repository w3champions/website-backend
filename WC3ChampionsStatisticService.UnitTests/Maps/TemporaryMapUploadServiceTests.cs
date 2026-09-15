using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using W3ChampionsStatisticService.Maps;
using W3ChampionsStatisticService.Sessions;

namespace WC3ChampionsStatisticService.Tests.Maps;

/// <summary>
/// The §6.3 new-map path and the sha1 dedupe hit: quota, the update-service digest check and the capture validation.
/// Restores live in <see cref="TemporaryMapUploadServiceRestoreTests"/>; conflicts, ambiguous writes, cancellation and
/// compensation in <see cref="TemporaryMapUploadServiceFailureTests"/>.
/// </summary>
[TestFixture]
public class TemporaryMapUploadServiceTests : TemporaryMapUploadServiceTestBase
{
    [Test]
    public async Task NewMap_Uploads_Verifies_CreatesTheRecord_AndReturnsNoSecret()
    {
        var handler = StoredNewMapHandler()
            .On(IsCreate, Respond(HttpStatusCode.Created, Record(5811)));
        var counts = new UploadCounts();

        var outcome = await Run(handler);

        Assert.That(outcome.Created, Is.True);
        Assert.That(outcome.Response.MapId, Is.EqualTo(5811));
        Assert.That(outcome.Response.Path, Is.EqualTo(FileKey));
        Assert.That(outcome.Response.Name, Is.EqualTo("Legion TD"));
        Assert.That(outcome.Response.Sha1, Is.EqualTo(Sha1));
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.Created);

        var responseJson = JsonConvert.SerializeObject(outcome.Response);
        Assert.That(responseJson, Does.Not.Contain(MapProofValue), "the upload response carries no secret");
        Assert.That(responseJson, Does.Not.Contain(ProofHash));

        var usBody = handler.RequestBodies[handler.Requests.FindIndex(r => IsUsUpload(r))];
        Assert.That(usBody, Does.Contain(UsFileName), "fileName is the fileKey minus the W3Champions/ prefix");
        Assert.That(usBody, Does.Match("name=\"?mapId\"?\r\n(?:[^\r\n]+\r\n)*\r\n0\r\n"), "a new map is stored with mapId 0");
        Assert.That(usBody, Does.Contain(BattleTag));

        var mmBody = JObject.Parse(handler.RequestBodies[handler.Requests.FindIndex(r => IsCreate(r))]);
        Assert.That(mmBody["sha1"]!.Value<string>(), Is.EqualTo(Sha1));
        Assert.That(mmBody["mapProof"]!.Value<string>(), Is.EqualTo(MapProofValue), "the server-computed proof, not the client's");
        Assert.That(mmBody["uploader"]!.Value<string>(), Is.EqualTo(BattleTag));
        Assert.That(mmBody["originalFileName"]!.Value<string>(), Is.EqualTo("Legion TD.w3x"));
        Assert.That(mmBody["lobbyMode"]!.Value<string>(), Is.EqualTo("mapped-forces"));
        Assert.That(mmBody["maxTeams"]!.Value<int>(), Is.EqualTo(2));
        Assert.That(mmBody["slotCount"]!.Value<int>(), Is.EqualTo(16));
        Assert.That(mmBody["mappedForces"]![0]!["slots"]![0]!["index"]!.Value<int>(), Is.Zero);
        Assert.That(mmBody["gameMap"]!["path"]!.Value<string>(), Is.EqualTo(@"maps\W3Champions\CustomGames\Legion TD-a9993e36.w3x"));
        Assert.That(mmBody["gameMap"]!["sha1"]!.Value<string>(), Is.EqualTo(Sha1));
        Assert.That(mmBody["gameMap"]!["num_players"]!.Value<int>(), Is.EqualTo(16), "the parsed metadata is forwarded");
        Assert.That(Count(handler, IsUsDelete), Is.Zero, "nothing to compensate on the happy path");
    }

    [Test]
    public async Task ExistingPresentRecord_IsADedupeHit_WithNoFileStoredAndNoQuotaSpent()
    {
        var handler = new ScriptedHttpHandler().On(IsBySha1, Respond(HttpStatusCode.OK, Record(5811)));
        var limiter = new MintRateLimiter();
        var counts = new UploadCounts();

        var outcome = await Run(handler, limiter);

        Assert.That(outcome.Created, Is.False);
        Assert.That(outcome.Response.MapId, Is.EqualTo(5811));
        Assert.That(outcome.Response.Path, Is.EqualTo(FileKey));
        Assert.That(outcome.Response.Sha1, Is.EqualTo(Sha1));
        Assert.That(handler.Requests, Has.Count.EqualTo(1), "only the dedupe probe: no bytes stored, no record written");
        Assert.That(limiter.Count, Is.Zero, "a dedupe hit must not consume upload quota");
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.Deduped);
    }

    [TestCase("")]
    [TestCase("expired")]
    [TestCase("PRESENT")]
    public void ADedupeRecordWithAnUnknownFileState_Is502_WithNothingStored(string fileState)
    {
        var handler = new ScriptedHttpHandler().On(IsBySha1, Respond(HttpStatusCode.OK, Record(5811, fileState: fileState)));
        var counts = new UploadCounts();

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler));

        Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.Status502BadGateway));
        Assert.That(ex.Code, Is.EqualTo("UPSTREAM"));
        Assert.That(handler.Requests, Has.Count.EqualTo(1), "neither a restore nor a new record is attempted");
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.UpstreamError);
    }

    [Test]
    public void ClientSha1NotMatchingTheBytes_Is400_Sha1Mismatch()
    {
        var handler = new ScriptedHttpHandler();
        var counts = new UploadCounts();

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler, metadataSha1: OtherSha1));

        Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
        Assert.That(ex.Code, Is.EqualTo("SHA1_MISMATCH"));
        Assert.That(handler.Requests, Is.Empty, "nothing upstream is contacted");
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.Rejected);
    }

    [Test]
    public async Task AnUppercaseClientSha1_IsTheSameHint()
    {
        var handler = new ScriptedHttpHandler().On(IsBySha1, Respond(HttpStatusCode.OK, Record(5811)));

        var outcome = await Run(handler, metadataSha1: Sha1.ToUpperInvariant());

        Assert.That(outcome.Response.MapId, Is.EqualTo(5811));
    }

    [Test]
    public void ARejectionFromTheReader_IsCountedOnce_AndEscapesUnchanged()
    {
        var handler = new ScriptedHttpHandler();
        var counts = new UploadCounts();
        var (body, contentType) = BuildMultipart("{\"sha1\":\"" + Sha1 + "\",\"originalFileName\":\"Legion TD.zip\"}", "abc"u8.ToArray());

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(
            () => CreateService(handler).HandleUploadAsync(body, contentType, BattleTag, default));

        Assert.That(ex.Code, Is.EqualTo("EXTENSION"));
        Assert.That(handler.Requests, Is.Empty);
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.Rejected);
    }

    [Test]
    public void EleventhNewMapInAnHour_Is429_WithRetryAfterSeconds()
    {
        var limiter = new MintRateLimiter();
        var now = DateTime.UtcNow;
        for (var i = 0; i < TemporaryMapLimits.UploadsPerHourPerBattleTag; i++)
        {
            limiter.TryAcquire("tm-upload:" + BattleTag, TemporaryMapLimits.UploadsPerHourPerBattleTag, now,
                TemporaryMapLimits.UploadQuotaWindow, out _);
        }

        var handler = UnknownSha1Handler();
        var counts = new UploadCounts();

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler, limiter));

        Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.Status429TooManyRequests));
        Assert.That(ex.Code, Is.EqualTo("QUOTA_EXCEEDED"));
        var body = JObject.Parse(JsonConvert.SerializeObject(ex.Body));
        Assert.That(body.Properties(), Has.Exactly(2).Items);
        Assert.That(body["code"]!.Value<string>(), Is.EqualTo("QUOTA_EXCEEDED"));
        Assert.That(body["retryAfterSeconds"]!.Value<int>(), Is.InRange(3500, 3600));
        Assert.That(Count(handler, IsUsUpload), Is.Zero);
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.Rejected);
    }

    [Test]
    public void AQuotaWindowThatSaturatesTheLimiter_AnswersIntMaxValue_NotAWrappedCast()
    {
        // A window opened in the future with the largest length makes the limiter report TimeSpan.MaxValue.
        var limiter = new MintRateLimiter();
        var future = DateTime.UtcNow.AddDays(1);
        for (var i = 0; i < TemporaryMapLimits.UploadsPerHourPerBattleTag; i++)
        {
            limiter.TryAcquire("tm-upload:" + BattleTag, TemporaryMapLimits.UploadsPerHourPerBattleTag, future, TimeSpan.MaxValue, out _);
        }

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(UnknownSha1Handler(), limiter));

        Assert.That(JObject.Parse(JsonConvert.SerializeObject(ex.Body))["retryAfterSeconds"]!.Value<int>(), Is.EqualTo(int.MaxValue));
    }

    [TestCase(-10_000_000L, 1, TestName = "RetryAfterSeconds(minus 1 s) is 1")]
    [TestCase(0L, 1, TestName = "RetryAfterSeconds(zero) is 1")]
    [TestCase(1L, 1, TestName = "RetryAfterSeconds(one tick) is 1")]
    [TestCase(2_000_000L, 1, TestName = "RetryAfterSeconds(200 ms) is 1")]
    [TestCase(10_000_000L, 1, TestName = "RetryAfterSeconds(exactly 1 s) is 1")]
    [TestCase(10_000_001L, 2, TestName = "RetryAfterSeconds(1 s and a tick) is 2")]
    [TestCase(35_995_000_000L, 3600, TestName = "RetryAfterSeconds(3599.5 s) is 3600")]
    [TestCase(21_474_836_470_000_000L, int.MaxValue, TestName = "RetryAfterSeconds(int.MaxValue s) is int.MaxValue")]
    [TestCase(21_474_836_475_000_000L, int.MaxValue, TestName = "RetryAfterSeconds(int.MaxValue + 0.5 s) is int.MaxValue")]
    [TestCase(long.MaxValue, int.MaxValue, TestName = "RetryAfterSeconds(TimeSpan.MaxValue) is int.MaxValue")]
    public void RetryAfterSeconds_RoundsUp_AndClampsBeforeTheCast(long ticks, int expected)
    {
        Assert.That(TemporaryMapUploadService.RetryAfterSeconds(TimeSpan.FromTicks(ticks)), Is.EqualTo(expected));
    }

    [TestCase(Sha1, "0000000000000000000000000000000000000000000000000000000000000000", TestName = "ForeignProofHash")]
    [TestCase(OtherSha1, ProofHash, TestName = "ForeignSha1")]
    [TestCase("A9993E364706816ABA3E25717850C26C9CD0D89D", "6E60C9A01CFD09CEC0B8493FE1AD9F2411ED21A1E1E58AAB9ECEEB3E9BAC2BD6", TestName = "UppercaseProofHash")]
    public void UpdateServiceDerivingADifferentDigest_Is502_ParserMismatch_AndCompensates(string metaSha1, string mapProofHash)
    {
        var handler = UnknownSha1Handler()
            .On(IsUsUpload, Respond(HttpStatusCode.OK, UsUploadBody(metaSha1, mapProofHash)))
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""));
        var counts = new UploadCounts();

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler));

        Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.Status502BadGateway));
        Assert.That(ex.Code, Is.EqualTo("PARSER_MISMATCH"));
        Assert.That(Count(handler, IsUsDelete), Is.EqualTo(1));
        Assert.That(handler.LastRequest(HttpMethod.Delete, "/api/content/maps/file")!.RequestUri!.Query,
            Is.EqualTo("?filePath=" + System.Web.HttpUtility.UrlEncode(FileKey)), "our own fileKey is compensated");
        Assert.That(Count(handler, IsCreate), Is.Zero);
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.UpstreamError);
    }

    [Test]
    public void UpdateServiceAnsweringWithoutMetadata_Is502_ParserMismatch_AndCompensates()
    {
        var handler = UnknownSha1Handler()
            .On(IsUsUpload, Respond(HttpStatusCode.OK, "{\"id\":\"66f0\",\"mapId\":0,\"mapProofHash\":\"" + ProofHash + "\"}"))
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""));

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler));

        Assert.That(ex.Code, Is.EqualTo("PARSER_MISMATCH"));
        Assert.That(Count(handler, IsUsDelete), Is.EqualTo(1));
    }

    [Test]
    public void AnUppercaseParsedSha1_StillMatches()
    {
        var handler = UnknownSha1Handler()
            .On(IsUsUpload, Respond(HttpStatusCode.OK, UsUploadBody(metaSha1: Sha1.ToUpperInvariant())))
            .On(IsCreate, Respond(HttpStatusCode.Created, Record(5811)));

        Assert.DoesNotThrowAsync(() => Run(handler));
        Assert.That(JObject.Parse(handler.RequestBodies[handler.Requests.FindIndex(r => IsCreate(r))])["gameMap"]!["sha1"]!.Value<string>(),
            Is.EqualTo(Sha1), "matchmaking receives the lowercase sha1");
    }

    [TestCase(25, "mapped-forces", 2, false, TestName = "slotCount above the 24-slot ceiling")]
    [TestCase(13, "mapped-forces", 2, true, TestName = "slotCount above the 12-slot ceiling of a twelve_p map")]
    [TestCase(0, "mapped-forces", 2, false, TestName = "slotCount below one")]
    [TestCase(16, "free", 2, false, TestName = "free lobby mode with non-empty mappedForces")]
    [TestCase(16, "mapped-forces", 0, false, TestName = "maxTeams below one")]
    [TestCase(16, "Mapped-Forces", 2, false, TestName = "an unknown lobbyMode")]
    public void AnImpossibleCapture_Is400_InvalidLayout_AndCompensates(int slotCount, string lobbyMode, int maxTeams, bool twelveP)
    {
        var handler = UnknownSha1Handler()
            .On(IsUsUpload, Respond(HttpStatusCode.OK, UsUploadBody(twelveP: twelveP)))
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""));
        var counts = new UploadCounts();

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler, capture: CaptureJson(slotCount, lobbyMode, maxTeams)));

        Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
        Assert.That(ex.Code, Is.EqualTo("INVALID_LAYOUT"));
        Assert.That(Count(handler, IsUsDelete), Is.EqualTo(1));
        Assert.That(Count(handler, IsCreate), Is.Zero);
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.Rejected);
    }

    [TestCase("null", TestName = "mappedForces null")]
    [TestCase("[null]", TestName = "a null force")]
    [TestCase("[{\"team\":0,\"slots\":null}]", TestName = "a force with null slots")]
    [TestCase("[{\"team\":0}]", TestName = "a force without slots")]
    [TestCase("[{\"team\":0,\"slots\":[null]}]", TestName = "a null slot")]
    [TestCase("[{\"team\":0,\"slots\":[{\"index\":-1}]}]", TestName = "a negative slot index")]
    [TestCase("[{\"team\":0,\"slots\":[{\"index\":16}]}]", TestName = "a slot index equal to slotCount")]
    [TestCase("[{\"team\":0,\"slots\":[{\"index\":0},{\"index\":1}]},{\"team\":1,\"slots\":[{\"index\":1},{\"index\":2}]}]",
        TestName = "a slot index used twice across forces")]
    [TestCase("[{\"team\":0,\"slots\":[{\"index\":3},{\"index\":3}]}]", TestName = "a slot index used twice in one force")]
    public void AMalformedMappedForce_Is400_InvalidLayout_AndCompensates(string mappedForcesJson)
    {
        var handler = StoredNewMapHandler().On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""));

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler, capture: CaptureJsonWithForces(mappedForcesJson)));

        Assert.That(ex.Code, Is.EqualTo("INVALID_LAYOUT"));
        Assert.That(Count(handler, IsUsDelete), Is.EqualTo(1));
        Assert.That(Count(handler, IsCreate), Is.Zero);
    }

    [TestCase(0, TestName = "a free lobby with zero slots")]
    [TestCase(-1, TestName = "a free lobby with a negative slotCount")]
    public void AFreeLobbyWithoutASlot_Is400_InvalidLayout(int slotCount)
    {
        // No force carries a seat index here, so only the slotCount bound can reject the capture.
        var handler = StoredNewMapHandler().On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""));

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(
            () => Run(handler, capture: CaptureJsonWithForces("[]", slotCount, "free", maxTeams: 1)));

        Assert.That(ex.Code, Is.EqualTo("INVALID_LAYOUT"));
        Assert.That(Count(handler, IsCreate), Is.Zero);
    }

    [Test]
    public void ANewMapWithoutACapture_Is400_InvalidLayout_AndCompensates()
    {
        // The known race: a record swept between a ready/expired pre-check and this upload. wb cannot invent a layout.
        var handler = StoredNewMapHandler().On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""));

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler, withCapture: false));

        Assert.That(ex.Code, Is.EqualTo("INVALID_LAYOUT"));
        Assert.That(Count(handler, IsUsDelete), Is.EqualTo(1));
        Assert.That(Count(handler, IsCreate), Is.Zero);
    }

    [TestCase(24, "mapped-forces", "[{\"team\":0,\"slots\":[{\"index\":0}]},{\"team\":1,\"slots\":[{\"index\":23}]}]", false,
        TestName = "24 slots with the last index")]
    [TestCase(12, "mapped-forces", "[{\"team\":0,\"slots\":[{\"index\":11}]}]", true, TestName = "12 slots on a twelve_p map")]
    [TestCase(1, "free", "[]", false, TestName = "a free lobby with one slot and no forces")]
    public async Task AValidCaptureAtItsLimits_IsAccepted(int slotCount, string lobbyMode, string mappedForcesJson, bool twelveP)
    {
        var handler = UnknownSha1Handler()
            .On(IsUsUpload, Respond(HttpStatusCode.OK, UsUploadBody(twelveP: twelveP)))
            .On(IsCreate, Respond(HttpStatusCode.Created, Record(5811)));

        var outcome = await Run(handler, capture: CaptureJsonWithForces(mappedForcesJson, slotCount, lobbyMode, maxTeams: 1));

        Assert.That(outcome.Created, Is.True);
        Assert.That(Count(handler, IsUsDelete), Is.Zero);
    }

    [TestCase(HttpStatusCode.BadRequest)]
    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.InternalServerError)]
    [TestCase(HttpStatusCode.ServiceUnavailable)]
    public void MatchmakingRefusingTheRecord_Compensates_AndIs502(HttpStatusCode status)
    {
        var handler = StoredNewMapHandler()
            .On(IsCreate, Respond(status, "{\"errors\":[{\"param\":\"gameMap\",\"message\":\"boom\"}]}"))
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""));
        var counts = new UploadCounts();

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler));

        Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.Status502BadGateway));
        Assert.That(ex.Code, Is.EqualTo("UPSTREAM"));
        Assert.That(Count(handler, IsUsDelete), Is.EqualTo(1));
        Assert.That(Count(handler, IsBySha1), Is.EqualTo(1), "a definite refusal needs no re-probe");
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.UpstreamError);
    }

    [Test]
    public async Task MatchmakingConflictOnTheSamePath_KeepsTheStoredBytesAndReturnsTheExistingRecord()
    {
        var handler = StoredNewMapHandler().On(IsCreate, Respond(HttpStatusCode.Conflict, Record(99)));
        var counts = new UploadCounts();

        var outcome = await Run(handler);

        Assert.That(outcome.Created, Is.False);
        Assert.That(outcome.Response.MapId, Is.EqualTo(99));
        Assert.That(Count(handler, IsUsDelete), Is.Zero, "the existing record points at the same bytes, so deleting them would break it");
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.Deduped);
    }

    [Test]
    public async Task MatchmakingConflictOnADifferentPath_CompensatesOurStrayFile()
    {
        const string olderPath = "W3Champions/CustomGames/older-a9993e36.w3x";
        var handler = StoredNewMapHandler()
            .On(IsCreate, Respond(HttpStatusCode.Conflict, Record(99, olderPath)))
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""));
        var counts = new UploadCounts();

        var outcome = await Run(handler);

        Assert.That(outcome.Created, Is.False);
        Assert.That(outcome.Response.Path, Is.EqualTo(olderPath));
        Assert.That(Count(handler, IsUsDelete), Is.EqualTo(1));
        Assert.That(handler.LastRequest(HttpMethod.Delete, "/api/content/maps/file")!.RequestUri!.Query,
            Is.EqualTo("?filePath=" + System.Web.HttpUtility.UrlEncode(FileKey)), "only our own fileKey is deleted, never the winner's");
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.Deduped);
    }
}
