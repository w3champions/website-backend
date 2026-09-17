using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using W3C.Contracts.Matchmaking;
using W3C.Domain.MatchmakingService;
using W3C.Domain.UpdateService;
using W3ChampionsStatisticService.Maps;
using W3ChampionsStatisticService.WebApi.ExceptionFilters;
using WC3ChampionsStatisticService.Tests.WebApi;

namespace WC3ChampionsStatisticService.Tests.Maps;

/// <summary>
/// Upstream failures must always surface as an HttpRequestException carrying the upstream status, whatever the
/// error body holds: a proxy's HTML page, nothing at all, or JSON of another shape. Anything else (a
/// NullReferenceException, a JsonReaderException) escapes every status-based branch and becomes an unexplained 500.
/// </summary>
[TestFixture]
public class UpstreamErrorHandlingTests
{
    private const string HtmlGatewayPage = "<html><body><h1>502 Bad Gateway</h1></body></html>";

    [TestCase("")]
    [TestCase(HtmlGatewayPage)]
    [TestCase("{\"errors\":null}")]
    public void MatchmakingError_WithAnUnreadableBody_ThrowsWithTheUpstreamStatus(string body)
    {
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Post, "/maps/temporary", HttpStatusCode.BadGateway, body)
            .On(HttpMethod.Post, "/maps", HttpStatusCode.BadGateway, body);
        var client = new MatchmakingServiceClient(new ScriptedHttpHandler.Factory(handler));

        var created = Assert.ThrowsAsync<HttpRequestException>(() => client.CreateTemporaryMap(TemporaryMapClientTests.SampleCreateRequest()));
        var permanent = Assert.ThrowsAsync<HttpRequestException>(() => client.CreateMap(new MapContract { Name = "x" }));

        Assert.That(created!.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
        Assert.That(permanent!.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
    }

    [Test]
    public void MatchmakingError_KeepsItsParamAndMessageText()
    {
        // On a route whose request carries no proof: the proof-carrying calls never read an error body
        // (TemporaryMapClientContractTests.ProofCarryingCall_OnAnErrorStatus_ThrowsWithThatStatus_AndEchoesNothingOfTheBody).
        var handler = new ScriptedHttpHandler().On(HttpMethod.Post, "/maps/temporary/7/file-deleted", HttpStatusCode.BadRequest,
            "{\"errors\":[{\"param\":\"sha1\",\"msg\":\"does not match\"},{\"param\":\"uploader\",\"msg\":\"required\"}]}");
        var client = new MatchmakingServiceClient(new ScriptedHttpHandler.Factory(handler));

        var thrown = Assert.ThrowsAsync<HttpRequestException>(() => client.MarkTemporaryMapFileDeleted(7));

        Assert.That(thrown!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(thrown.Message, Is.EqualTo("sha1 does not match,uploader required"));
    }

    [TestCase("{}", TestName = "MatchmakingError_WithAnEmptyObjectBody_DescribesTheStatus")]
    [TestCase("{\"errors\":[]}", TestName = "MatchmakingError_WithAnEmptyErrorsArray_DescribesTheStatus")]
    [TestCase("{\"errors\":null}", TestName = "MatchmakingError_WithANullErrorsArray_DescribesTheStatus")]
    [TestCase("{\"errors\":[{}]}", TestName = "MatchmakingError_WhoseOnlyEntryIsEmpty_DescribesTheStatus")]
    [TestCase("{\"errors\":[null]}", TestName = "MatchmakingError_WhoseOnlyEntryIsNull_DescribesTheStatus")]
    public void MatchmakingError_WithoutAnErrorEntryThatSaysAnything_DescribesTheStatus(string body)
    {
        // matchmaking answers a bare {} (e.g. file-deleted for an id that is not a temporary map) and its validator
        // may send an entries-less array; ErrorResponse deserialises both to an empty Errors array. An empty message
        // would leave the log and the 502 body saying nothing, so the status-only text applies to those as well.
        var handler = new ScriptedHttpHandler().On(HttpMethod.Post, "/maps/temporary/7/file-deleted", HttpStatusCode.NotFound, body);
        var client = new MatchmakingServiceClient(new ScriptedHttpHandler.Factory(handler));

        var thrown = Assert.ThrowsAsync<HttpRequestException>(() => client.MarkTemporaryMapFileDeleted(7));

        Assert.That(thrown!.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(thrown.Message, Is.EqualTo("matchmaking-service returned 404"));
    }

    [TestCase("{\"errors\":[{\"msg\":\"x is required\",\"path\":\"x\"}]}", "x x is required",
        TestName = "MatchmakingError_ReadsTheFieldNameFromExpressValidatorV7Path")]
    [TestCase("{\"errors\":[{\"msg\":\"x is required\",\"param\":\"x\"}]}", "x x is required",
        TestName = "MatchmakingError_ReadsTheFieldNameFromExpressValidatorV6Param")]
    [TestCase("{\"errors\":[{\"msg\":\"x is required\",\"param\":\"old\",\"path\":\"new\"}]}", "new x is required",
        TestName = "MatchmakingError_PrefersPathOverParam")]
    [TestCase("{\"errors\":[{\"msg\":\"x is required\"}]}", "x is required",
        TestName = "MatchmakingError_WithoutAFieldName_IsJustTheMessage")]
    [TestCase("{\"errors\":[{\"path\":\"x\"}]}", "x",
        TestName = "MatchmakingError_WithoutAMessage_IsJustTheFieldName")]
    [TestCase("{\"errors\":[{\"msg\":\"a\",\"path\":\"x\"},{\"msg\":\"b\"},{\"msg\":\"c\",\"param\":\"z\"}]}", "x a,b,z c",
        TestName = "MatchmakingError_JoinsEveryEntryWithoutStrayWhitespace")]
    public void MatchmakingError_NamesTheFieldAndTheMessageOfEveryEntry(string body, string expected)
    {
        var handler = new ScriptedHttpHandler().On(HttpMethod.Post, "/maps/temporary/7/file-deleted", HttpStatusCode.UnprocessableEntity, body);
        var client = new MatchmakingServiceClient(new ScriptedHttpHandler.Factory(handler));

        var thrown = Assert.ThrowsAsync<HttpRequestException>(() => client.MarkTemporaryMapFileDeleted(7));

        Assert.That(thrown!.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
        Assert.That(thrown.Message, Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase(HtmlGatewayPage)]
    public void UpdateServiceMapFileReads_WithAnUnreadableErrorBody_ThrowWithTheUpstreamStatus(string body)
    {
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Get, "/api/content/maps?mapId=7", HttpStatusCode.BadGateway, body)
            .On(HttpMethod.Get, "/api/content/maps/f1", HttpStatusCode.NotFound, body);
        var client = new UpdateServiceClient(new ScriptedHttpHandler.Factory(handler));

        var files = Assert.ThrowsAsync<HttpRequestException>(() => client.GetMapFiles(7));
        var file = Assert.ThrowsAsync<HttpRequestException>(() => client.GetMapFile("f1"));

        Assert.That(files!.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
        Assert.That(file!.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    private static readonly Dictionary<string, Func<MapsController, Task<IActionResult>>> ControllerActions = new()
    {
        ["CreateMap"] = c => c.CreateMap(new MapContract { Name = "x" }, "Admin#1"),
        ["UpdateMap"] = c => c.UpdateMap(7, new MapContract { Name = "x" }, "Admin#1"),
        ["GetMapFiles"] = c => c.GetMapFiles(7),
        ["CreateMapFile"] = c => c.CreateMapFile("Admin#1"),
        ["GetMapFile"] = c => c.GetMapFile("f1"),
        ["DeleteMapFile"] = c => c.DeleteMapFile("f1"),
    };

    private static IEnumerable<string> ControllerActionNames() => ControllerActions.Keys;

    [TestCaseSource(nameof(ControllerActionNames))]
    public async Task MapsControllerAction_WhenTheUpstreamIsUnreachable_AnswersLikeTheGlobalExceptionFilter(string action)
    {
        // A transport failure (connection refused, DNS, TLS) is an HttpRequestException WITHOUT a status code, and
        // its message names the upstream host and port.
        var transportFailure = new HttpRequestException("Connection refused (mm.internal.example:3000)");
        var handler = new ScriptedHttpHandler().On(_ => true, _ => throw transportFailure);
        var logger = new Mock<ILogger<MapsController>>();

        var result = await ControllerActions[action](CreateController(handler, logger));

        Assert.That(result, Is.InstanceOf<ObjectResult>());
        Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(StatusCodes.Status502BadGateway),
            "HttpRequestExceptionFilter answers 502 for a status-less HttpRequestException; the per-action catch must agree");
        Assert.That(((ObjectResult)result).Value, Is.EqualTo(HttpRequestExceptionFilter.TransportFailureMessage),
            "the transport failure's own text names an internal host");
        Assert.That(handler.Requests.Any(), Is.True, action + " never reached the upstream");
        var entry = HttpRequestExceptionFilterTests.LogEntries(logger).Single();
        Assert.That(entry.Level, Is.EqualTo(LogLevel.Error));
        Assert.That(entry.Exception, Is.SameAs(transportFailure));
        Assert.That(entry.Message, Does.Contain(action).And.Contain("502"));
    }

    [Test]
    public async Task CreateMapFile_WhenTheForwardOutlivesTheUploadTimeout_AnswersLikeATransportFailure()
    {
        // HttpClient's own timeout is a TaskCanceledException, not an HttpRequestException: neither the per-action
        // catch nor the global filter would see it, and the admin would get an unexplained 500 for a forward that
        // simply took too long. The temporary upload answers the same case as an upstream failure.
        var timeout = new TaskCanceledException("simulated HttpClient timeout");
        var handler = new ScriptedHttpHandler().On(_ => true, _ => throw timeout);
        var logger = new Mock<ILogger<MapsController>>();

        var result = await ControllerActions["CreateMapFile"](CreateController(handler, logger));

        Assert.That(result, Is.InstanceOf<ObjectResult>());
        Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(StatusCodes.Status502BadGateway));
        Assert.That(((ObjectResult)result).Value, Is.EqualTo(HttpRequestExceptionFilter.TransportFailureMessage));
        var entry = HttpRequestExceptionFilterTests.LogEntries(logger).Single();
        Assert.That(entry.Level, Is.EqualTo(LogLevel.Error));
        Assert.That(entry.Exception, Is.SameAs(timeout));
        Assert.That(entry.Message, Does.Contain("CreateMapFile").And.Contain("502"));
    }

    private static IEnumerable<TestCaseData> ActionsWithSuccessStatuses()
        => from action in ControllerActions.Keys
           from status in new[] { HttpStatusCode.OK, HttpStatusCode.Created }
           select new TestCaseData(action, status).SetName($"MapsControllerAction_{action}_FailureCarrying{(int)status}_AnswersBadGateway");

    [TestCaseSource(nameof(ActionsWithSuccessStatuses))]
    public async Task MapsControllerAction_WhenTheFailureCarriesASuccessStatus_AnswersBadGateway(string action, HttpStatusCode status)
    {
        // A contract violation carries the upstream's own success status (UpstreamContract); relaying it would answer
        // an error body with 200 or 201.
        const string violation = "update-service answered with a body that breaks its contract";
        var handler = new ScriptedHttpHandler().On(_ => true, _ => throw new HttpRequestException(violation, null, status));
        var logger = new Mock<ILogger<MapsController>>();

        var result = await ControllerActions[action](CreateController(handler, logger));

        var objectResult = (ObjectResult)result;
        Assert.That(objectResult.StatusCode, Is.EqualTo(StatusCodes.Status502BadGateway));
        Assert.That(objectResult.Value, Is.EqualTo(violation));
        var entry = HttpRequestExceptionFilterTests.LogEntries(logger).Single();
        Assert.That(entry.Level, Is.EqualTo(LogLevel.Error));
        Assert.That(entry.Exception, Is.Null);
        Assert.That(entry.Message, Does.Contain(action).And.Contain("502").And.Contain(((int)status).ToString()));
    }

    [TestCase("GetMapFiles")]
    [TestCase("CreateMapFile")]
    [TestCase("GetMapFile")]
    public async Task MapsControllerMapFileAction_WhenUpdateServiceAnswersAnUnreadableSuccess_AnswersBadGateway(string action)
    {
        // Before, such a body escaped as a JsonReaderException and became an unexplained 500.
        var handler = new ScriptedHttpHandler().On(_ => true, _ => ScriptedHttpHandler.Json(HttpStatusCode.OK, HtmlGatewayPage));
        var logger = new Mock<ILogger<MapsController>>();

        var result = await ControllerActions[action](CreateController(handler, logger));

        var objectResult = (ObjectResult)result;
        Assert.That(objectResult.StatusCode, Is.EqualTo(StatusCodes.Status502BadGateway));
        Assert.That((string)objectResult.Value, Does.Not.Contain("html").And.Not.Contain("://"));
        Assert.That(HttpRequestExceptionFilterTests.LogEntries(logger).Single().Level, Is.EqualTo(LogLevel.Error));
    }

    [TestCaseSource(nameof(ControllerActionNames))]
    public async Task MapsControllerAction_WhenTheUpstreamAnswersAnError_KeepsStatusAndMessage_AndLogsActionAndStatus(string action)
    {
        var handler = new ScriptedHttpHandler().On(_ => true, _ => ScriptedHttpHandler.Json(HttpStatusCode.BadGateway, HtmlGatewayPage));
        var logger = new Mock<ILogger<MapsController>>();

        var result = await ControllerActions[action](CreateController(handler, logger));

        var objectResult = (ObjectResult)result;
        Assert.That(objectResult.StatusCode, Is.EqualTo(StatusCodes.Status502BadGateway));
        Assert.That(objectResult.Value, Is.InstanceOf<string>().And.Not.Empty.And.Not.EqualTo(HttpRequestExceptionFilter.TransportFailureMessage));
        var entry = HttpRequestExceptionFilterTests.LogEntries(logger).Single();
        Assert.That(entry.Level, Is.EqualTo(LogLevel.Error));
        Assert.That(entry.Exception, Is.Null, "a status-bearing exception's message can hold an upstream body");
        Assert.That(entry.Message, Does.Contain(action).And.Contain("502"));
    }

    private static MapsController CreateController(ScriptedHttpHandler handler, Mock<ILogger<MapsController>> logger)
    {
        var factory = new ScriptedHttpHandler.Factory(handler);
        return new MapsController(new MatchmakingServiceClient(factory), new UpdateServiceClient(factory), logger.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = AdminRequest() },
        };
    }

    private static DefaultHttpContext AdminRequest()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("website-backend.test");
        context.Request.Path = "/api/maps/7/files";
        return context;
    }
}
