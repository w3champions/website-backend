using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using W3C.Contracts.Matchmaking;
using W3C.Domain.MatchmakingService;
using W3C.Domain.MatchmakingService.Contracts;
using W3C.Domain.UpdateService;
using W3ChampionsStatisticService.Maps;

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
        var handler = new ScriptedHttpHandler().On(HttpMethod.Post, "/maps/temporary/7/file-restored", HttpStatusCode.BadRequest,
            "{\"errors\":[{\"param\":\"sha1\",\"msg\":\"does not match\"},{\"param\":\"uploader\",\"msg\":\"required\"}]}");
        var client = new MatchmakingServiceClient(new ScriptedHttpHandler.Factory(handler));

        var thrown = Assert.ThrowsAsync<HttpRequestException>(() =>
            client.MarkTemporaryMapFileRestored(7, new TemporaryMapFileRestoredRequest { Sha1 = TemporaryMapClientTests.Sha1 }));

        Assert.That(thrown!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(thrown.Message, Is.EqualTo("sha1 does not match,uploader required"));
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
        // A transport failure (connection refused, DNS, TLS) is an HttpRequestException WITHOUT a status code.
        var handler = new ScriptedHttpHandler().On(_ => true, _ => throw new HttpRequestException("upstream unreachable"));
        var factory = new ScriptedHttpHandler.Factory(handler);
        var controller = new MapsController(new MatchmakingServiceClient(factory), new UpdateServiceClient(factory))
        {
            ControllerContext = new ControllerContext { HttpContext = AdminRequest() },
        };

        var result = await ControllerActions[action](controller);

        Assert.That(result, Is.InstanceOf<ObjectResult>());
        Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(StatusCodes.Status500InternalServerError),
            "HttpRequestExceptionFilter answers 500 for a status-less HttpRequestException; the per-action catch must agree");
        Assert.That(handler.Requests.Any(), Is.True, action + " never reached the upstream");
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
