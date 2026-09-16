using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using W3C.Domain.MatchmakingService;
using W3C.Domain.MatchmakingService.Contracts;
using W3C.Domain.Tracing;
using W3C.Domain.UpdateService;
using W3ChampionsStatisticService.Maps;
using W3ChampionsStatisticService.Sessions;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace WC3ChampionsStatisticService.Tests.Maps;

/// <summary>
/// GET api/maps/temporary/status (Appendix A.4 with the segment B rulings) and the shape of both routes: the state and
/// nothing else, a bare 429, a bare 502 for anything matchmaking cannot answer, an empty result for a client that is
/// gone, and a 401 when the auth filter left no battleTag behind.
/// </summary>
[TestFixture]
public class TemporaryMapsControllerTests : TemporaryMapUploadServiceTestBase
{
    private const string ByProofHash = "/maps/temporary/by-proof-hash/";

    [TestCase("present", "ready")]
    [TestCase("deleted", "expired")]
    public async Task Status_MapsTheFileStateOntoTheClientState(string fileState, string expected)
    {
        var handler = new ScriptedHttpHandler().On(HttpMethod.Get, ByProofHash, HttpStatusCode.OK, "{\"fileState\":\"" + fileState + "\"}");

        var result = await CreateController(handler).GetStatus(ProofHash, CancellationToken.None) as OkObjectResult;

        Assert.That(result, Is.Not.Null);
        var body = JObject.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(result!.Value));
        Assert.That(body["state"]!.Value<string>(), Is.EqualTo(expected));
        Assert.That(body.Properties().Count(), Is.EqualTo(1), "the pre-check returns state and nothing else — no id, path, name, sha1 or proof");
        Assert.That(handler.LastRequest(HttpMethod.Get, ByProofHash).RequestUri!.AbsolutePath, Does.EndWith("/" + ProofHash));
    }

    [Test]
    public async Task Status_ReturnsUnknownWhenMatchmakingHasNoRecord()
    {
        var handler = new ScriptedHttpHandler().On(HttpMethod.Get, ByProofHash, HttpStatusCode.NotFound);

        var result = await CreateController(handler).GetStatus(ProofHash, CancellationToken.None) as NotFoundObjectResult;

        Assert.That(result, Is.Not.Null);
        var body = JObject.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(result!.Value));
        Assert.That(body["state"]!.Value<string>(), Is.EqualTo("unknown"));
        Assert.That(body.Properties().Count(), Is.EqualTo(1));
    }

    [TestCase("")]
    [TestCase(null)]
    [TestCase("not-hex")]
    [TestCase("6E60C9A01CFD09CEC0B8493FE1AD9F2411ED21A1E1E58AAB9ECEEB3E9BAC2BD6", TestName = "Status_TreatsAMalformedProofHashAsUnknown(uppercase)")]
    [TestCase("6e60c9a0", TestName = "Status_TreatsAMalformedProofHashAsUnknown(too short)")]
    [TestCase(ProofHash + "0", TestName = "Status_TreatsAMalformedProofHashAsUnknown(too long)")]
    [TestCase(" " + ProofHash, TestName = "Status_TreatsAMalformedProofHashAsUnknown(leading space)")]
    [TestCase("../../maps/temporary/by-sha1/a9993e364706816aba3e25717850c26c9cd0d89dxx", TestName = "Status_TreatsAMalformedProofHashAsUnknown(path shape)")]
    public async Task Status_TreatsAMalformedProofHashAsUnknown_WithoutAskingMatchmaking(string proofHash)
    {
        var handler = new ScriptedHttpHandler();

        var result = await CreateController(handler).GetStatus(proofHash, CancellationToken.None) as NotFoundObjectResult;

        Assert.That(result, Is.Not.Null);
        Assert.That(JObject.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(result!.Value))["state"]!.Value<string>(), Is.EqualTo("unknown"));
        Assert.That(handler.Requests, Is.Empty, "a malformed pre-check must not become an upstream probe");
    }

    [Test]
    public async Task Status_RateLimitsPerBattleTag_WithABareBody()
    {
        var handler = new ScriptedHttpHandler().On(HttpMethod.Get, ByProofHash, HttpStatusCode.NotFound);
        var limiter = new MintRateLimiter();
        var controller = CreateController(handler, limiter);
        for (var i = 0; i < TemporaryMapLimits.PrecheckPerBattleTagPerMinute; i++)
        {
            Assert.That(await controller.GetStatus(ProofHash, CancellationToken.None), Is.InstanceOf<NotFoundObjectResult>());
        }

        var throttled = await controller.GetStatus(ProofHash, CancellationToken.None) as StatusCodeResult;

        Assert.That(throttled, Is.Not.Null, "the pre-check 429 carries no body at all (Appendix A.4)");
        Assert.That(throttled!.StatusCode, Is.EqualTo(StatusCodes.Status429TooManyRequests));
        Assert.That(handler.CountRequests(HttpMethod.Get, ByProofHash), Is.EqualTo(TemporaryMapLimits.PrecheckPerBattleTagPerMinute),
            "a throttled pre-check never reaches matchmaking");
        Assert.That(TemporaryMapLimits.PrecheckPerBattleTagPerMinute, Is.EqualTo(60));

        var other = CreateController(handler, limiter, battleTag: OtherBattleTag);
        Assert.That(await other.GetStatus(ProofHash, CancellationToken.None), Is.InstanceOf<NotFoundObjectResult>(),
            "the quota is per battleTag");
    }

    [Test]
    public async Task Status_SharesTheLimiterWithTheUploadQuota_UnderItsOwnKey()
    {
        // One MintRateLimiter instance serves both routes (Program.cs registers a single one): the pre-check window must
        // not touch the upload window of the same battleTag.
        var limiter = new MintRateLimiter();
        var handler = new ScriptedHttpHandler().On(HttpMethod.Get, ByProofHash, HttpStatusCode.NotFound);
        var controller = CreateController(handler, limiter);
        for (var i = 0; i < TemporaryMapLimits.PrecheckPerBattleTagPerMinute; i++)
        {
            await controller.GetStatus(ProofHash, CancellationToken.None);
        }

        Assert.That(limiter.TryAcquire("tm-upload:" + BattleTag, TemporaryMapLimits.UploadsPerHourPerBattleTag, DateTime.UtcNow,
            TemporaryMapLimits.UploadQuotaWindow, out _), Is.True, "the exhausted pre-check window is not the upload window");
    }

    [TestCase("gone")]
    [TestCase("PRESENT")]
    [TestCase("Deleted")]
    public async Task Status_AnUnknownFileState_Is502_WithNoBody_AndWarns(string fileState)
    {
        var handler = new ScriptedHttpHandler().On(HttpMethod.Get, ByProofHash, HttpStatusCode.OK, "{\"fileState\":\"" + fileState + "\"}");
        using var logs = new LogCapture();

        var result = await CreateController(handler, logger: logs.CreateLogger<TemporaryMapsController>()).GetStatus(ProofHash, CancellationToken.None);

        AssertBare502(result);
        var warnings = logs.Lines().Where(l => l.StartsWith("Warning", StringComparison.Ordinal)).ToArray();
        Assert.That(warnings, Has.Length.EqualTo(1));
        Assert.That(warnings[0], Does.Contain("FileState=\"" + fileState + "\""));
        AssertNoSecretIn(logs.Lines());
    }

    [Test]
    public async Task Status_AFileStateWithALineBreak_IsLoggedAsInvalid()
    {
        var handler = new ScriptedHttpHandler().On(HttpMethod.Get, ByProofHash, HttpStatusCode.OK, "{\"fileState\":\"gone\\u000d\\u000aforged line\"}");
        using var logs = new LogCapture();

        var result = await CreateController(handler, logger: logs.CreateLogger<TemporaryMapsController>()).GetStatus(ProofHash, CancellationToken.None);

        AssertBare502(result);
        Assert.That(logs.Lines().Any(l => l.Contains("forged", StringComparison.Ordinal)), Is.False, "the value was rendered as sent");
        Assert.That(logs.Lines().Count(l => l.Contains("=\"invalid\"", StringComparison.Ordinal)), Is.EqualTo(1));
    }

    private static readonly object[] MatchmakingFailures =
    [
        new object[] { "transport failure", (Func<HttpRequestMessage, HttpResponseMessage>)(_ => throw new HttpRequestException("connection refused (matchmaking.example:3000)")) },
        new object[] { "HttpClient timeout", (Func<HttpRequestMessage, HttpResponseMessage>)(_ => throw new TaskCanceledException("simulated HttpClient timeout")) },
        new object[] { "route 404 with an HTML body", Respond(HttpStatusCode.NotFound, "<html>Cannot GET /maps/temporary/by-proof-hash/" + ProofHash + "</html>") },
        new object[] { "404 with a non-empty JSON body", Respond(HttpStatusCode.NotFound, "{\"state\":\"unknown\"}") },
        new object[] { "500 with an error body", Respond(HttpStatusCode.InternalServerError, "{\"errors\":[{\"param\":\"proofHash\",\"message\":\"boom " + ProofHash + "\"}]}") },
        new object[] { "200 without a fileState", Respond(HttpStatusCode.OK, "{}") },
        new object[] { "200 that is not JSON", Respond(HttpStatusCode.OK, "<html>maintenance</html>") },
        new object[] { "302 redirect", Respond(HttpStatusCode.Found, "") },
    ];

    [TestCaseSource(nameof(MatchmakingFailures))]
    public async Task Status_AMatchmakingFailure_Is502_WithNoBody_AndWarnsWithoutTheProofHash(
        string _, Func<HttpRequestMessage, HttpResponseMessage> matchmaking)
    {
        var handler = new ScriptedHttpHandler().On(r => r.RequestUri!.AbsolutePath.Contains(ByProofHash, StringComparison.Ordinal), matchmaking);
        using var logs = new LogCapture();

        var result = await CreateController(handler, logger: logs.CreateLogger<TemporaryMapsController>()).GetStatus(ProofHash, CancellationToken.None);

        AssertBare502(result);
        var warnings = logs.Lines().Where(l => l.StartsWith("Warning", StringComparison.Ordinal)).ToArray();
        Assert.That(warnings, Has.Length.EqualTo(1), "one Warning, nothing at Error: an upstream outage is expected noise");
        Assert.That(logs.Lines().Where(l => l.StartsWith("Error", StringComparison.Ordinal)), Is.Empty);
        Assert.That(warnings[0], Does.Contain("Exception"), "the exception type is logged");
        Assert.That(warnings[0], Does.Not.Contain("simulated").And.Not.Contain("boom").And.Not.Contain("matchmaking.example"),
            "never the exception or its message: matchmaking echoes request text, which can hold the proofHash");
        AssertNoSecretIn(logs.Lines());
    }

    [Test]
    public async Task Status_AClientAbort_IsAnEmptyResult_AndPassesTheRequestTokenToMatchmaking()
    {
        // The client goes away while matchmaking answers: the answer is read with the request token, so the read is
        // what cancels. With CancellationToken.None instead, the read would succeed and the state would be answered.
        using var aborted = new CancellationTokenSource();
        var handler = new ScriptedHttpHandler().On(r => r.RequestUri!.AbsolutePath.Contains(ByProofHash, StringComparison.Ordinal), _ =>
        {
            aborted.Cancel();
            return ScriptedHttpHandler.Json(HttpStatusCode.OK, "{\"fileState\":\"present\"}");
        });
        using var logs = new LogCapture();
        var controller = CreateController(handler, logger: logs.CreateLogger<TemporaryMapsController>());
        controller.HttpContext.RequestAborted = aborted.Token;

        var result = await controller.GetStatus(ProofHash, aborted.Token);

        Assert.That(result, Is.InstanceOf<EmptyResult>(), "nobody is listening");
        Assert.That(handler.CountRequests(HttpMethod.Get, ByProofHash), Is.EqualTo(1));
        Assert.That(logs.Lines().Where(l => l.StartsWith("Warning", StringComparison.Ordinal) || l.StartsWith("Error", StringComparison.Ordinal)), Is.Empty,
            "a client abort is not an upstream failure");
    }

    [Test]
    public async Task Status_AContractViolationRacingTheClientAbort_IsAnEmptyResult_AndNotAWarning()
    {
        // The one failure that reaches this route as something other than a cancellation once the client is gone: the
        // body was read in full, the abort landed, and only then did the client reject the body (200 without a fileState).
        // HttpClient itself turns every transport failure under a cancelled token into a cancellation. Whatever escaped,
        // nobody is listening, and an abort is not upstream noise worth a Warning.
        using var aborted = new CancellationTokenSource();
        var handler = new ScriptedHttpHandler().On(r => r.RequestUri!.AbsolutePath.Contains(ByProofHash, StringComparison.Ordinal),
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new AbortingAtEndStream("{}"u8.ToArray(), aborted)) });
        using var logs = new LogCapture();
        var controller = CreateController(handler, logger: logs.CreateLogger<TemporaryMapsController>());
        controller.HttpContext.RequestAborted = aborted.Token;

        var result = await controller.GetStatus(ProofHash, aborted.Token);

        Assert.That(result, Is.InstanceOf<EmptyResult>());
        Assert.That(logs.Lines().Where(l => l.StartsWith("Warning", StringComparison.Ordinal) || l.StartsWith("Error", StringComparison.Ordinal)), Is.Empty);
    }

    [Test]
    public async Task Status_ACancellationTheClientDidNotCause_Is502()
    {
        // The request token is not cancelled, yet the call was: an HttpClient timeout, i.e. an upstream failure.
        using var notTheRequest = new CancellationTokenSource();
        notTheRequest.Cancel();
        var handler = new ScriptedHttpHandler().On(HttpMethod.Get, ByProofHash, HttpStatusCode.OK, "{\"fileState\":\"present\"}");
        var controller = CreateController(handler);

        var result = await controller.GetStatus(ProofHash, notTheRequest.Token);

        AssertBare502(result);
    }

    [TestCase(null)]
    [TestCase("")]
    public void Status_ARecordWithoutAFileState_Is502_WithNoBody_AndWarns(string fileState)
    {
        // The matchmaking client refuses such a record itself, so this cannot be reached through it today; the route's
        // own validation holds regardless: only "present" and "deleted" are relayed, a missing value is not "unknown".
        using var logs = new LogCapture();
        var controller = CreateController(new ScriptedHttpHandler(), logger: logs.CreateLogger<TemporaryMapsController>());

        var result = controller.AnswerState(new TemporaryMapStateResponse { FileState = fileState });

        AssertBare502(result);
        var warnings = logs.Lines().Where(l => l.StartsWith("Warning", StringComparison.Ordinal)).ToArray();
        Assert.That(warnings, Has.Length.EqualTo(1));
        Assert.That(warnings[0], Does.Contain("FileState=\"invalid\""));
    }

    [Test]
    public void Status_NoRecord_IsUnknown_AndNothingIsLogged()
    {
        using var logs = new LogCapture();
        var controller = CreateController(new ScriptedHttpHandler(), logger: logs.CreateLogger<TemporaryMapsController>());

        var result = controller.AnswerState(null) as NotFoundObjectResult;

        Assert.That(result, Is.Not.Null);
        Assert.That(JObject.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(result!.Value))["state"]!.Value<string>(), Is.EqualTo("unknown"));
        Assert.That(logs.Lines(), Is.Empty);
    }

    [TestCase(null)]
    [TestCase("")]
    public async Task Status_FailsClosed_WithoutABattleTag(string battleTag)
    {
        var handler = new ScriptedHttpHandler().On(HttpMethod.Get, ByProofHash, HttpStatusCode.OK, "{\"fileState\":\"present\"}");
        var controller = CreateController(handler, battleTag: battleTag);

        var result = await controller.GetStatus(ProofHash, CancellationToken.None) as StatusCodeResult;

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(StatusCodes.Status401Unauthorized));
        Assert.That(handler.Requests, Is.Empty);
    }

    [Test]
    public async Task Status_FailsClosed_WhenTheBattleTagItemIsMissingAltogether()
    {
        var handler = new ScriptedHttpHandler().On(HttpMethod.Get, ByProofHash, HttpStatusCode.OK, "{\"fileState\":\"present\"}");
        var controller = CreateController(handler);
        controller.HttpContext.Items.Remove(BearerRequiresPlayerAuthFilter.BattleTagItemKey);

        var result = await controller.GetStatus(ProofHash, CancellationToken.None) as StatusCodeResult;

        Assert.That(result?.StatusCode, Is.EqualTo(StatusCodes.Status401Unauthorized));
        Assert.That(handler.Requests, Is.Empty);
    }

    // ---- Route and filter shape -----------------------------------------------------------

    [Test]
    public void BothRoutesRequirePlayerAuth()
    {
        foreach (var methodName in new[] { nameof(TemporaryMapsController.GetStatus), nameof(TemporaryMapsController.Upload) })
        {
            var method = typeof(TemporaryMapsController).GetMethod(methodName)!;
            Assert.That(method.GetCustomAttribute<BearerRequiresPlayerAuthAttribute>(), Is.Not.Null, methodName + " must be player-authenticated");
            Assert.That(method.GetCustomAttribute<BearerHasPermissionFilter>(), Is.Null, methodName + " is player-facing and must NOT require an admin permission");
        }
    }

    [Test]
    public void UploadScopesTheBodyLimitAndSuppressesFormBinding()
    {
        var upload = typeof(TemporaryMapsController).GetMethod(nameof(TemporaryMapsController.Upload))!;

        Assert.That(upload.GetCustomAttribute<TemporaryMapUploadBodyLimitAttribute>(), Is.Not.Null);
        Assert.That(upload.GetCustomAttribute<DisableFormValueModelBindingAttribute>(), Is.Not.Null);
        Assert.That(typeof(TemporaryMapsController).GetMethods()
                .Any(m => m.GetCustomAttribute<TemporaryMapUploadBodyLimitAttribute>() != null && m.Name != nameof(TemporaryMapsController.Upload)),
            Is.False, "the 257 MiB ceiling must not leak onto any other action");
    }

    [Test]
    public void ControllerIsRoutedAtApiMapsTemporary()
    {
        var route = typeof(TemporaryMapsController).GetCustomAttribute<RouteAttribute>();
        Assert.That(route, Is.Not.Null);
        Assert.That(route!.Template, Is.EqualTo("api/maps/temporary"));
        Assert.That(typeof(TemporaryMapsController).GetCustomAttribute<ApiControllerAttribute>(), Is.Null,
            "the [ApiController] client-error mapping would give the A.4 bare answers a ProblemDetails body");

        var status = typeof(TemporaryMapsController).GetMethod(nameof(TemporaryMapsController.GetStatus))!.GetCustomAttribute<HttpGetAttribute>();
        Assert.That(status?.Template, Is.EqualTo("status"));
        var upload = typeof(TemporaryMapsController).GetMethod(nameof(TemporaryMapsController.Upload))!.GetCustomAttribute<HttpPostAttribute>();
        Assert.That(upload, Is.Not.Null);
        Assert.That(upload!.Template, Is.Null.Or.Empty, "POST api/maps/temporary itself");
    }

    [Test]
    public void TheProofHashParameter_IsNeverTraced()
    {
        // The controller class is [Trace]d like MapsController; should controllers ever be intercepted, the query value
        // must not become a param.proofHash activity tag (spec §10.3).
        var parameter = typeof(TemporaryMapsController).GetMethod(nameof(TemporaryMapsController.GetStatus))!
            .GetParameters().Single(p => p.Name == "proofHash");

        Assert.That(parameter.GetCustomAttribute<NoTraceAttribute>(), Is.Not.Null);
        Assert.That(parameter.GetCustomAttribute<FromQueryAttribute>(), Is.Not.Null);
    }

    // ---- Helpers --------------------------------------------------------------------------

    private static void AssertBare502(IActionResult result)
    {
        // StatusCodeResult and its subclasses write a status only; an ObjectResult is not one of them.
        Assert.That(result, Is.InstanceOf<StatusCodeResult>(), "a bare status, no body (A.4: nothing else is returned)");
        Assert.That(((StatusCodeResult)result).StatusCode, Is.EqualTo(StatusCodes.Status502BadGateway));
    }

    private static void AssertNoSecretIn(string[] lines)
    {
        foreach (var secret in new[] { ProofHash, ProofHash.ToUpperInvariant(), MapProofValue })
        {
            Assert.That(lines, Has.None.Contains(secret));
        }
    }

    private TemporaryMapsController CreateController(
        ScriptedHttpHandler handler,
        MintRateLimiter limiter = null,
        ILogger<TemporaryMapsController> logger = null,
        string battleTag = BattleTag)
    {
        var factory = new ScriptedHttpHandler.Factory(handler);
        var matchmaking = new MatchmakingServiceClient(factory);

        // One limiter instance for the controller and the upload service, exactly as Program.cs registers it.
        var rateLimiter = limiter ?? new MintRateLimiter();
        var controller = new TemporaryMapsController(
            new TemporaryMapUploadService(matchmaking, new UpdateServiceClient(factory), rateLimiter, FileKeyLock, NullLogger<TemporaryMapUploadService>.Instance)
            {
                SpoolDirectory = SpoolDirectory,
            },
            matchmaking,
            rateLimiter,
            new TemporaryMapUploadGate(),
            logger ?? NullLogger<TemporaryMapsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        controller.HttpContext.Items[BearerRequiresPlayerAuthFilter.BattleTagItemKey] = battleTag;
        return controller;
    }

    /// <summary>A response body that is delivered whole, then aborts the request as it reports its end.</summary>
    private sealed class AbortingAtEndStream(byte[] bytes, CancellationTokenSource abort) : MemoryStream(bytes)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await base.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                abort.Cancel();
            }

            return read;
        }
    }
}
