using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using W3C.Contracts.Admin.Permission;
using W3C.Domain.MatchmakingService;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Admin;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.Services;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace WC3ChampionsStatisticService.Tests.Admin;

[TestFixture]
public class AdminWarningsControllerTests
{
    [Test]
    public void WarningEndpointsRequireWarningsPermission()
    {
        AssertWarningsPermission(nameof(AdminController.GetWarnings));
        AssertWarningsPermission(nameof(AdminController.CreateWarning));
        AssertWarningsPermission(nameof(AdminController.CancelWarning));
    }

    [Test]
    public async Task CreateWarningCanonicalizesTargetInjectsIssuerAndForwardsAdminSecret()
    {
        var handler = new CapturingHandler();
        var resolver = new Mock<IBattleTagResolver>();
        resolver.Setup(r => r.ResolveCanonical("grubby#1234")).ReturnsAsync("Grubby#1234");
        var controller = CreateController(handler, resolver.Object);

        var result = await controller.CreateWarning(new CreatePlayerWarningRequest
        {
            targetBattleTag = "grubby#1234",
            severity = "",
            title = new Dictionary<string, string> { ["en"] = "Careful" },
            body = new Dictionary<string, string> { ["en"] = "Please mind rule 2." },
        }, "Admin#1");

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        Assert.That(handler.Requests, Has.Count.EqualTo(1));
        Assert.That(handler.Requests[0].Headers.Contains("x-admin-secret"), Is.True);

        var body = JObject.Parse(handler.RequestBodies[0]);
        Assert.That(body["targetBattleTag"]!.Value<string>(), Is.EqualTo("Grubby#1234"));
        Assert.That(body["issuedByBattleTag"]!.Value<string>(), Is.EqualTo("Admin#1"));
        Assert.That(body["rule"], Is.Null);
        Assert.That(body["category"], Is.Null);
        Assert.That(body["severity"]!.Value<string>(), Is.EqualTo("Warning"));
    }

    [Test]
    public async Task GetWarningsCanonicalizesBattleTagFilter()
    {
        var handler = new CapturingHandler("{\"total\":0,\"warnings\":[]}");
        var resolver = new Mock<IBattleTagResolver>();
        resolver.Setup(r => r.ResolveCanonical("grubby#1234")).ReturnsAsync("Grubby#1234");
        var controller = CreateController(handler, resolver.Object);

        var result = await controller.GetWarnings(new PlayerWarningsGetRequest
        {
            BattleTag = "grubby#1234",
            Page = 1,
            ItemsPerPage = 10,
        });

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        Assert.That(handler.Requests, Has.Count.EqualTo(1));
        Assert.That(handler.Requests[0].RequestUri!.Query, Does.Contain("battleTag=Grubby%231234"));
    }

    [Test]
    public async Task GetWarningsRejectsUnknownBattleTagFilterBeforeProxying()
    {
        var handler = new CapturingHandler();
        var resolver = new Mock<IBattleTagResolver>();
        resolver.Setup(r => r.ResolveCanonical("missing#1")).ReturnsAsync((string)null);
        var controller = CreateController(handler, resolver.Object);

        var result = await controller.GetWarnings(new PlayerWarningsGetRequest
        {
            BattleTag = "missing#1",
            Page = 1,
            ItemsPerPage = 10,
        });

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        Assert.That(handler.Requests, Is.Empty);
    }

    [Test]
    public async Task CreateWarningRejectsUnknownTargetBeforeProxying()
    {
        var handler = new CapturingHandler();
        var resolver = new Mock<IBattleTagResolver>();
        resolver.Setup(r => r.ResolveCanonical("missing#1")).ReturnsAsync((string)null);
        var controller = CreateController(handler, resolver.Object);

        var result = await controller.CreateWarning(new CreatePlayerWarningRequest
        {
            targetBattleTag = "missing#1",
            severity = "Warning",
            title = new Dictionary<string, string> { ["en"] = "Careful" },
            body = new Dictionary<string, string> { ["en"] = "Please mind rule 2." },
        }, "Admin#1");

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        Assert.That(handler.Requests, Is.Empty);
    }

    [Test]
    public async Task CancelWarningInjectsAdminBattleTagAndForwardsAdminSecret()
    {
        var handler = new CapturingHandler("{\"_id\":\"warning-1\",\"targetBattleTag\":\"Grubby#1234\",\"issuedByBattleTag\":\"Admin#1\",\"rule\":\"Rule 2\",\"severity\":\"Warning\",\"title\":{\"en\":\"Careful\"},\"body\":{\"en\":\"Please mind rule 2.\"},\"status\":\"Cancelled\",\"createdAt\":\"2026-06-06T20:00:00Z\"}");
        var controller = CreateController(handler, Mock.Of<IBattleTagResolver>());

        var result = await controller.CancelWarning("warning-1", "Admin#1");

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        Assert.That(handler.Requests, Has.Count.EqualTo(1));
        Assert.That(handler.Requests[0].Headers.Contains("x-admin-secret"), Is.True);
        Assert.That(handler.Requests[0].RequestUri!.AbsolutePath, Does.EndWith("/admin/warnings/warning-1/cancel"));

        var body = JObject.Parse(handler.RequestBodies[0]);
        Assert.That(body["cancelledByBattleTag"]!.Value<string>(), Is.EqualTo("Admin#1"));
    }

    private static void AssertWarningsPermission(string methodName)
    {
        var method = typeof(AdminController).GetMethod(methodName)!;
        var attribute = method.GetCustomAttribute<BearerHasPermissionFilter>();

        Assert.That(attribute, Is.Not.Null);
        Assert.That(attribute!.Permission, Is.EqualTo(EPermission.Warnings));
    }

    private static AdminController CreateController(CapturingHandler handler, IBattleTagResolver resolver)
    {
        var client = new MatchmakingServiceClient(new TestHttpClientFactory(new HttpClient(handler)));

        return new AdminController(
            Mock.Of<IMatchRepository>(),
            client,
            Mock.Of<INewsRepository>(),
            Mock.Of<IInformationMessagesRepository>(),
            Mock.Of<IAdminRepository>(),
            Mock.Of<IRankRepository>(),
            resolver);
    }

    private class TestHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private class CapturingHandler(string responseBody = "{\"warning\":{\"_id\":\"warning-1\",\"targetBattleTag\":\"Grubby#1234\",\"issuedByBattleTag\":\"Admin#1\",\"rule\":\"Rule 2\",\"severity\":\"Warning\",\"title\":{\"en\":\"Careful\"},\"body\":{\"en\":\"Please mind rule 2.\"},\"status\":\"Pending\",\"createdAt\":\"2026-06-06T20:00:00Z\"},\"delivered\":false}") : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            RequestBodies.Add(request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
