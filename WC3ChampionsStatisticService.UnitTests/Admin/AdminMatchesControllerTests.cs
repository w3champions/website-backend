using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using W3C.Contracts.Admin.Permission;
using W3C.Contracts.Matchmaking;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Admin;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace WC3ChampionsStatisticService.Tests.Admin;

[TestFixture]
public class AdminMatchesControllerTests
{
    private const string CanceledMatchesJson = """
    {"total":3,"matches":[{"_id":"abc1234567","state":3,"gameMode":5,"floGameId":4242,
    "players":[{"battleTag":"Tester#1234","team":0,"slotIndex":2}]}]}
    """;

    [Test]
    public void CanceledMatchesRequiresModerationPermission()
    {
        var method = typeof(AdminMatchesController).GetMethod(nameof(AdminMatchesController.GetCanceledMatches))!;
        var attribute = method.GetCustomAttribute<BearerHasPermissionFilter>();

        Assert.That(attribute, Is.Not.Null);
        Assert.That(attribute!.Permission, Is.EqualTo(EPermission.Moderation));
    }

    // Regression guard: BearerHasPermissionFilter.OnActionExecutionAsync (BearerHasPermissionFilter.cs:33)
    // unconditionally does `context.ActionArguments["battleTag"] = res.BattleTag;` on every action it
    // guards, overwriting that argument with the acting admin's own tag. That is intentional for
    // audit-style parameters (e.g. AdminController.CreateWarningDefinition's trailing `battleTag`), but
    // GetCanceledMatches's battle tag argument is a caller-supplied search filter, not an audit field. If
    // a parameter here is ever named exactly "battleTag" again, the filter would silently replace the
    // moderator's search value with their own battletag through the real HTTP pipeline -- invisible to
    // the other tests in this file because they call the controller method directly and never run
    // BearerHasPermissionFilter. Do not "clean up" this parameter name back to "battleTag".
    [Test]
    public void GetCanceledMatchesHasNoParameterNamedBattleTag()
    {
        var method = typeof(AdminMatchesController).GetMethod(nameof(AdminMatchesController.GetCanceledMatches))!;

        Assert.That(method.GetCustomAttribute<BearerHasPermissionFilter>(), Is.Not.Null,
            "This guard only matters while the action is BearerHasPermissionFilter-decorated.");
        Assert.That(method.GetParameters().Select(p => p.Name), Has.None.EqualTo("battleTag"));
    }

    [Test]
    public async Task CanceledMatchesForwardsFiltersAndAdminSecret()
    {
        var handler = new CapturingHandler(CanceledMatchesJson);
        var controller = CreateController(handler);

        var result = await controller.GetCanceledMatches(GameMode.FFA, "Tester#1234", 2, 10);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        Assert.That(handler.Requests, Has.Count.EqualTo(1));
        var uri = handler.Requests[0].RequestUri!;
        Assert.That(uri.AbsolutePath, Does.EndWith("/admin/canceled-matches"));
        Assert.That(uri.Query, Does.Contain("page=2"));
        Assert.That(uri.Query, Does.Contain("itemsPerPage=10"));
        Assert.That(uri.Query, Does.Contain("gameMode=5"));
        Assert.That(uri.Query, Does.Contain("battleTag=Tester"));
        Assert.That(handler.Requests[0].Headers.Contains("x-admin-secret"), Is.True);
    }

    [Test]
    public async Task AllGameModesOmitsTheGameModeFilter()
    {
        var handler = new CapturingHandler(CanceledMatchesJson);
        var controller = CreateController(handler);

        await controller.GetCanceledMatches(GameMode.Undefined, null, 1, 25);

        Assert.That(handler.Requests[0].RequestUri!.Query, Does.Not.Contain("gameMode="));
    }

    [Test]
    public async Task PageSizeIsClampedBeforeItLeavesTheService()
    {
        var handler = new CapturingHandler(CanceledMatchesJson);
        var controller = CreateController(handler);

        await controller.GetCanceledMatches(GameMode.Undefined, null, 1, 5000);

        Assert.That(handler.Requests[0].RequestUri!.Query, Does.Contain("itemsPerPage=100"));
    }

    [Test]
    public async Task TheProxiedPayloadKeepsIdsSlotIndexesAndTotal()
    {
        var handler = new CapturingHandler(CanceledMatchesJson);
        var controller = CreateController(handler);

        var result = (OkObjectResult)await controller.GetCanceledMatches(GameMode.Undefined, null, 1, 25);
        var payload = (CanceledMatchesResponse)result.Value!;

        Assert.That(payload.total, Is.EqualTo(3));
        Assert.That(payload.matches[0].id, Is.EqualTo("abc1234567"));
        Assert.That(payload.matches[0].floGameId, Is.EqualTo(4242));
        Assert.That(payload.matches[0].players[0].slotIndex, Is.EqualTo(2));
    }

    private static AdminMatchesController CreateController(CapturingHandler handler)
    {
        var client = new MatchmakingServiceClient(new TestHttpClientFactory(new HttpClient(handler)));
        return new AdminMatchesController(client);
    }

    private class TestHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private class CapturingHandler(string responseBody) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            });
        }
    }
}
