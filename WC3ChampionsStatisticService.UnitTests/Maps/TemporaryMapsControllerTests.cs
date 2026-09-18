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
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using W3C.Domain.Maps;
using W3C.Domain.MatchmakingService;
using W3C.Domain.MatchmakingService.Contracts;
using W3C.Domain.UpdateService;
using W3ChampionsStatisticService.Maps;
using W3ChampionsStatisticService.Sessions;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace WC3ChampionsStatisticService.Tests.Maps;

/// <summary>
/// GET api/maps/temporary/status (Appendix A.4, revision 10: the proofHash arrives in the x-proof-hash request header,
/// the ?proofHash= query form is removed and never consulted) and the shape of both routes: the state and nothing
/// else, a bare 429, a bare 502 for anything matchmaking cannot answer, an empty result for a client that is gone,
/// and a 401 when the auth filter left no battleTag behind.
/// </summary>
[TestFixture]
public class TemporaryMapsControllerTests : TemporaryMapUploadServiceTestBase
{
    private const string ByProofHash = "/maps/temporary/by-proof-hash";

    [TestCase("present", "ready")]
    [TestCase("deleted", "expired")]
    public async Task Status_MapsTheFileStateOntoTheClientState(string fileState, string expected)
    {
        var handler = new ScriptedHttpHandler().On(HttpMethod.Post, ByProofHash, HttpStatusCode.OK, "{\"fileState\":\"" + fileState + "\"}");

        var result = await CreateController(handler).GetStatus(CancellationToken.None) as OkObjectResult;

        Assert.That(result, Is.Not.Null);
        var body = JObject.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(result!.Value));
        Assert.That(body["state"]!.Value<string>(), Is.EqualTo(expected));
        Assert.That(body.Properties().Count(), Is.EqualTo(1), "the pre-check returns state and nothing else — no id, path, name, sha1 or proof");
        var request = handler.LastRequest(HttpMethod.Post, ByProofHash);
        Assert.That(request.RequestUri!.PathAndQuery, Does.EndWith(ByProofHash), "the proofHash travels in the body, never in the URL");
        Assert.That(JObject.Parse(handler.LastBody(HttpMethod.Post, ByProofHash))["proofHash"]!.Value<string>(), Is.EqualTo(ProofHash));
    }

    [Test]
    public async Task Status_ReturnsUnknownWhenMatchmakingHasNoRecord()
    {
        var handler = new ScriptedHttpHandler().On(HttpMethod.Post, ByProofHash, HttpStatusCode.NotFound);

        var result = await CreateController(handler).GetStatus(CancellationToken.None) as NotFoundObjectResult;

        Assert.That(result, Is.Not.Null);
        var body = JObject.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(result!.Value));
        Assert.That(body["state"]!.Value<string>(), Is.EqualTo("unknown"));
        Assert.That(body.Properties().Count(), Is.EqualTo(1));
    }

    [TestCase("", TestName = "Status_TreatsAMalformedProofHashHeaderAsUnknown(empty)")]
    [TestCase(null, TestName = "Status_TreatsAMalformedProofHashHeaderAsUnknown(missing)")]
    [TestCase("not-hex")]
    [TestCase("6E60C9A01CFD09CEC0B8493FE1AD9F2411ED21A1E1E58AAB9ECEEB3E9BAC2BD6", TestName = "Status_TreatsAMalformedProofHashHeaderAsUnknown(uppercase)")]
    [TestCase("6e60c9a0", TestName = "Status_TreatsAMalformedProofHashHeaderAsUnknown(too short)")]
    [TestCase("6e60c9a01cfd09cec0b8493fe1ad9f2411ed21a1e1e58aab9eceeb3e9bac2bd", TestName = "Status_TreatsAMalformedProofHashHeaderAsUnknown(63 chars)")]
    [TestCase(ProofHash + "0", TestName = "Status_TreatsAMalformedProofHashHeaderAsUnknown(too long)")]
    [TestCase(" " + ProofHash, TestName = "Status_TreatsAMalformedProofHashHeaderAsUnknown(leading space)")]
    [TestCase(ProofHash + ", " + ProofHash, TestName = "Status_TreatsAMalformedProofHashHeaderAsUnknown(two values on one line)")]
    [TestCase("../../maps/temporary/by-sha1/a9993e364706816aba3e25717850c26c9cd0d89dxx", TestName = "Status_TreatsAMalformedProofHashHeaderAsUnknown(path shape)")]
    public async Task Status_TreatsAMalformedProofHashHeaderAsUnknown_WithoutAskingMatchmaking(string proofHash)
    {
        // null: the header is absent altogether.
        var handler = new ScriptedHttpHandler();

        var result = await CreateController(handler, proofHash: proofHash).GetStatus(CancellationToken.None) as NotFoundObjectResult;

        AssertUnknown(result);
        Assert.That(handler.Requests, Is.Empty, "a malformed pre-check must not become an upstream probe");
    }

    [Test]
    public async Task Status_TreatsTwoProofHashHeaderValues_AsUnknown_WithoutAskingMatchmaking()
    {
        // Two x-proof-hash header lines (what Kestrel hands the action as two values): ambiguous, so malformed — never
        // the first one, never the last one.
        var handler = new ScriptedHttpHandler();
        var controller = CreateController(handler, proofHash: null);
        controller.HttpContext.Request.Headers[TemporaryMapKeys.ProofHashHeaderName] = new StringValues([ProofHash, ProofHash]);

        var result = await controller.GetStatus(CancellationToken.None) as NotFoundObjectResult;

        AssertUnknown(result);
        Assert.That(handler.Requests, Is.Empty);
    }

    [Test]
    public async Task Status_IgnoresTheRemovedProofHashQuery_AndAsksNothing()
    {
        // Revision 10 removed the ?proofHash= form: a client still sending it, and only it, is answered "unknown"
        // locally, and the value is never consulted, so it can never reach matchmaking.
        var handler = new ScriptedHttpHandler().On(HttpMethod.Post, ByProofHash, HttpStatusCode.OK, "{\"fileState\":\"present\"}");
        var controller = CreateController(handler, proofHash: null);
        controller.HttpContext.Request.QueryString = new QueryString("?proofHash=" + ProofHash);

        var result = await controller.GetStatus(CancellationToken.None) as NotFoundObjectResult;

        AssertUnknown(result);
        Assert.That(handler.Requests, Is.Empty, "the query value must never become the key of an upstream probe");
    }

    [Test]
    public async Task Status_ReadsTheHeader_AndIgnoresTheQuery_WhenBothAreSent()
    {
        var handler = new ScriptedHttpHandler().On(HttpMethod.Post, ByProofHash, HttpStatusCode.OK, "{\"fileState\":\"present\"}");
        var controller = CreateController(handler);
        controller.HttpContext.Request.QueryString = new QueryString("?proofHash=" + OtherProofHash);

        var result = await controller.GetStatus(CancellationToken.None) as OkObjectResult;

        Assert.That(result, Is.Not.Null);
        Assert.That(JObject.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(result!.Value))["state"]!.Value<string>(), Is.EqualTo("ready"));
        Assert.That(handler.Requests, Has.Count.EqualTo(1));
        Assert.That(JObject.Parse(handler.RequestBodies.Single())["proofHash"]!.Value<string>(), Is.EqualTo(ProofHash), "the header's value, not the query's");
        Assert.That(handler.RequestBodies.Single(), Does.Not.Contain(OtherProofHash));
    }

    [Test]
    public async Task Status_RateLimitsPerBattleTag_WithABareBody()
    {
        var handler = new ScriptedHttpHandler().On(HttpMethod.Post, ByProofHash, HttpStatusCode.NotFound);
        var limiter = new MintRateLimiter();
        var controller = CreateController(handler, limiter);
        for (var i = 0; i < TemporaryMapLimits.PrecheckPerBattleTagPerMinute; i++)
        {
            Assert.That(await controller.GetStatus(CancellationToken.None), Is.InstanceOf<NotFoundObjectResult>());
        }

        var throttled = await controller.GetStatus(CancellationToken.None) as StatusCodeResult;

        Assert.That(throttled, Is.Not.Null, "the pre-check 429 carries no body at all (Appendix A.4)");
        Assert.That(throttled!.StatusCode, Is.EqualTo(StatusCodes.Status429TooManyRequests));
        Assert.That(handler.CountRequests(HttpMethod.Post, ByProofHash), Is.EqualTo(TemporaryMapLimits.PrecheckPerBattleTagPerMinute),
            "a throttled pre-check never reaches matchmaking");
        Assert.That(TemporaryMapLimits.PrecheckPerBattleTagPerMinute, Is.EqualTo(60));

        var throttledWithoutAKey = await CreateController(handler, limiter, proofHash: "not-hex").GetStatus(CancellationToken.None) as StatusCodeResult;
        Assert.That(throttledWithoutAKey?.StatusCode, Is.EqualTo(StatusCodes.Status429TooManyRequests),
            "the quota is spent before the header is looked at: a throttled caller learns nothing about its key");

        var other = CreateController(handler, limiter, battleTag: OtherBattleTag);
        Assert.That(await other.GetStatus(CancellationToken.None), Is.InstanceOf<NotFoundObjectResult>(),
            "the quota is per battleTag");
    }

    [Test]
    public async Task Status_SharesTheLimiterWithTheUploadQuota_UnderItsOwnKey()
    {
        // One MintRateLimiter instance serves both routes (Program.cs registers a single one): the pre-check window must
        // not touch the upload window of the same battleTag.
        var limiter = new MintRateLimiter();
        var handler = new ScriptedHttpHandler().On(HttpMethod.Post, ByProofHash, HttpStatusCode.NotFound);
        var controller = CreateController(handler, limiter);
        for (var i = 0; i < TemporaryMapLimits.PrecheckPerBattleTagPerMinute; i++)
        {
            await controller.GetStatus(CancellationToken.None);
        }

        Assert.That(limiter.TryAcquire("tm-upload:" + BattleTag, TemporaryMapLimits.UploadsPerHourPerBattleTag, DateTime.UtcNow,
            TemporaryMapLimits.UploadQuotaWindow, out _), Is.True, "the exhausted pre-check window is not the upload window");
    }

    [TestCase("gone")]
    [TestCase("PRESENT")]
    [TestCase("Deleted")]
    public async Task Status_AnUnknownFileState_Is502_WithNoBody_AndWarns(string fileState)
    {
        var handler = new ScriptedHttpHandler().On(HttpMethod.Post, ByProofHash, HttpStatusCode.OK, "{\"fileState\":\"" + fileState + "\"}");
        using var logs = new LogCapture();

        var result = await CreateController(handler, logger: logs.CreateLogger<TemporaryMapsController>()).GetStatus(CancellationToken.None);

        AssertBare502(result);
        var warnings = logs.Lines().Where(l => l.StartsWith("Warning", StringComparison.Ordinal)).ToArray();
        Assert.That(warnings, Has.Length.EqualTo(1));
        Assert.That(warnings[0], Does.Contain("FileState=\"" + fileState + "\""));
        AssertNoSecretIn(logs.Lines());
    }

    [Test]
    public async Task Status_AFileStateWithALineBreak_IsLoggedAsInvalid()
    {
        var handler = new ScriptedHttpHandler().On(HttpMethod.Post, ByProofHash, HttpStatusCode.OK, "{\"fileState\":\"gone\\u000d\\u000aforged line\"}");
        using var logs = new LogCapture();

        var result = await CreateController(handler, logger: logs.CreateLogger<TemporaryMapsController>()).GetStatus(CancellationToken.None);

        AssertBare502(result);
        Assert.That(logs.Lines().Any(l => l.Contains("forged", StringComparison.Ordinal)), Is.False, "the value was rendered as sent");
        Assert.That(logs.Lines().Count(l => l.Contains("=\"invalid\"", StringComparison.Ordinal)), Is.EqualTo(1));
    }

    private static readonly object[] MatchmakingFailures =
    [
        new object[] { "transport failure", (Func<HttpRequestMessage, HttpResponseMessage>)(_ => throw new HttpRequestException("connection refused (matchmaking.example:3000)")) },
        new object[] { "HttpClient timeout", (Func<HttpRequestMessage, HttpResponseMessage>)(_ => throw new TaskCanceledException("simulated HttpClient timeout")) },
        new object[] { "route 404 with an HTML body", Respond(HttpStatusCode.NotFound, "<html>Cannot POST /maps/temporary/by-proof-hash</html>") },
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

        var result = await CreateController(handler, logger: logs.CreateLogger<TemporaryMapsController>()).GetStatus(CancellationToken.None);

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

        var result = await controller.GetStatus(aborted.Token);

        Assert.That(result, Is.InstanceOf<EmptyResult>(), "nobody is listening");
        Assert.That(handler.CountRequests(HttpMethod.Post, ByProofHash), Is.EqualTo(1));
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

        var result = await controller.GetStatus(aborted.Token);

        Assert.That(result, Is.InstanceOf<EmptyResult>());
        Assert.That(logs.Lines().Where(l => l.StartsWith("Warning", StringComparison.Ordinal) || l.StartsWith("Error", StringComparison.Ordinal)), Is.Empty);
    }

    [Test]
    public async Task Status_ACancellationTheClientDidNotCause_Is502()
    {
        // The request token is not cancelled, yet the call was: an HttpClient timeout, i.e. an upstream failure.
        using var notTheRequest = new CancellationTokenSource();
        notTheRequest.Cancel();
        var handler = new ScriptedHttpHandler().On(HttpMethod.Post, ByProofHash, HttpStatusCode.OK, "{\"fileState\":\"present\"}");
        var controller = CreateController(handler);

        var result = await controller.GetStatus(notTheRequest.Token);

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

    [TestCase(null, ProofHash)]
    [TestCase("", ProofHash)]
    [TestCase(null, "not-hex", TestName = "Status_FailsClosed_WithoutABattleTag_BeforeTheHeaderIsLookedAt")]
    public async Task Status_FailsClosed_WithoutABattleTag(string battleTag, string proofHash)
    {
        var handler = new ScriptedHttpHandler().On(HttpMethod.Post, ByProofHash, HttpStatusCode.OK, "{\"fileState\":\"present\"}");
        var controller = CreateController(handler, battleTag: battleTag, proofHash: proofHash);

        var result = await controller.GetStatus(CancellationToken.None) as StatusCodeResult;

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(StatusCodes.Status401Unauthorized));
        Assert.That(handler.Requests, Is.Empty);
    }

    [Test]
    public async Task Status_FailsClosed_WhenTheBattleTagItemIsMissingAltogether()
    {
        var handler = new ScriptedHttpHandler().On(HttpMethod.Post, ByProofHash, HttpStatusCode.OK, "{\"fileState\":\"present\"}");
        var controller = CreateController(handler);
        controller.HttpContext.Items.Remove(BearerRequiresPlayerAuthFilter.BattleTagItemKey);

        var result = await controller.GetStatus(CancellationToken.None) as StatusCodeResult;

        Assert.That(result?.StatusCode, Is.EqualTo(StatusCodes.Status401Unauthorized));
        Assert.That(handler.Requests, Is.Empty);
    }

    // ---- Route and filter shape -----------------------------------------------------------

    [Test]
    public void EveryActionRequiresPlayerAuth()
    {
        // Every public instance method the controller declares is an MVC action unless marked [NonAction]; the filter
        // is per method, so an action added without it would be anonymous. Enumerating rather than naming the two
        // known actions makes that omission a failing test, not a hole.
        var actions = typeof(TemporaryMapsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>() == null)
            .ToArray();

        Assert.That(actions.Select(m => m.Name), Is.EquivalentTo(new[] { nameof(TemporaryMapsController.GetStatus), nameof(TemporaryMapsController.Upload) }),
            "the two Appendix A routes and nothing else");
        foreach (var action in actions)
        {
            Assert.That(action.GetCustomAttribute<BearerRequiresPlayerAuthAttribute>(), Is.Not.Null, action.Name + " must be player-authenticated");
            Assert.That(action.GetCustomAttribute<BearerHasPermissionFilter>(), Is.Null, action.Name + " is player-facing and must NOT require an admin permission");
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
    public void TheStatusAction_BindsNoProofHashParameter_ItReadsTheHeaderItself()
    {
        // The controller class is [Trace]d like MapsController; should controllers ever be intercepted, every bound
        // argument would become a param.{name} activity tag (spec §10.3). The action binds nothing but the request
        // token: the header is read explicitly, under the Appendix A.4 name, and no query is bound anywhere.
        var status = typeof(TemporaryMapsController).GetMethod(nameof(TemporaryMapsController.GetStatus))!;

        Assert.That(status.GetParameters().Select(p => p.ParameterType), Is.EqualTo(new[] { typeof(CancellationToken) }));
        Assert.That(typeof(TemporaryMapsController).GetMethods().SelectMany(m => m.GetParameters())
                .Any(p => p.GetCustomAttribute<FromQueryAttribute>() != null || p.GetCustomAttribute<FromHeaderAttribute>() != null),
            Is.False, "nothing is model-bound from the query or the headers");
        Assert.That(TemporaryMapKeys.ProofHashHeaderName, Is.EqualTo("x-proof-hash"), "the Appendix A.4 header name, exactly");
    }

    // ---- Helpers --------------------------------------------------------------------------

    private static void AssertUnknown(NotFoundObjectResult result)
    {
        Assert.That(result, Is.Not.Null, "404 { state: \"unknown\" }");
        var body = JObject.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(result!.Value));
        Assert.That(body["state"]!.Value<string>(), Is.EqualTo("unknown"));
        Assert.That(body.Properties().Count(), Is.EqualTo(1));
    }

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

    /// <summary>A valid proofHash that is not the fixture's: a second key to tell apart from <see cref="ProofHash"/>.</summary>
    private const string OtherProofHash = "0000000000000000000000000000000000000000000000000000000000000000";

    /// <summary>
    /// The controller over the real clients, with the x-proof-hash request header set to <paramref name="proofHash"/>
    /// (null leaves the header out).
    /// </summary>
    private TemporaryMapsController CreateController(
        ScriptedHttpHandler handler,
        MintRateLimiter limiter = null,
        ILogger<TemporaryMapsController> logger = null,
        string battleTag = BattleTag,
        string proofHash = ProofHash)
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
        if (proofHash != null)
        {
            controller.HttpContext.Request.Headers[TemporaryMapKeys.ProofHashHeaderName] = proofHash;
        }

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
