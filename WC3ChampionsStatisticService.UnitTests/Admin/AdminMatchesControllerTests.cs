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
    {"nextCursor":"eyJmIjoiYWJjIn0.sig","matches":[{"_id":"abc1234567","state":3,"gameMode":5,"floGameId":4242,
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

        var result = await controller.GetCanceledMatches(GameMode.FFA, "Tester#1234", "prev-cursor", 10);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        Assert.That(handler.Requests, Has.Count.EqualTo(1));
        var uri = handler.Requests[0].RequestUri!;
        Assert.That(uri.AbsolutePath, Does.EndWith("/admin/canceled-matches"));
        Assert.That(uri.Query, Does.Contain("cursor=prev-cursor"));
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

        await controller.GetCanceledMatches(null, null, null, 25);

        // Null rather than GameMode.Undefined: "no filter" is now its own value instead
        // of enum member 0 doing double duty.
        Assert.That(handler.Requests[0].RequestUri!.Query, Does.Not.Contain("gameMode="));
    }

    [Test]
    public async Task PageSizeIsClampedBeforeItLeavesTheService()
    {
        var handler = new CapturingHandler(CanceledMatchesJson);
        var controller = CreateController(handler);

        await controller.GetCanceledMatches(null, null, null, 5000);

        Assert.That(handler.Requests[0].RequestUri!.Query, Does.Contain("itemsPerPage=100"));
    }

    [Test]
    public async Task TheProxiedPayloadKeepsIdsSlotIndexesAndTheCursor()
    {
        var handler = new CapturingHandler(CanceledMatchesJson);
        var controller = CreateController(handler);

        var result = (OkObjectResult)await controller.GetCanceledMatches(null, null, null, 25);
        var payload = (CanceledMatchesResponse)result.Value!;

        Assert.That(payload.nextCursor, Is.EqualTo("eyJmIjoiYWJjIn0.sig"));
        Assert.That(payload.matches[0].id, Is.EqualTo("abc1234567"));
        Assert.That(payload.matches[0].floGameId, Is.EqualTo(4242));
        Assert.That(payload.matches[0].players[0].slotIndex, Is.EqualTo(2));
    }


    [Test]
    public async Task AnUnreachableMatchmakingServiceIsABadGatewayRatherThanANotFound()
    {
        var handler = new CapturingHandler("upstream exploded", HttpStatusCode.InternalServerError);
        var controller = CreateController(handler);

        var result = (ObjectResult)await controller.GetCanceledMatches(null, null, null, 25);

        // Not 404: the request was fine and the collection exists, we just could not
        // reach the service that knows about it. A 404 would read as "there are no
        // cancelled matches", which a moderator might act on.
        Assert.That(result.StatusCode, Is.EqualTo(502));
    }

    [Test]
    public void ARejectedCursorSurfacesAsABadRequestNotABadGateway()
    {
        var handler = new CapturingHandler("{\"errors\":[{\"msg\":\"Pagination cursor failed signature validation.\"}]}", HttpStatusCode.BadRequest);
        var controller = CreateController(handler);

        // Matchmaking validates the cursor and the game mode, so its 400 is the caller's
        // fault. HttpRequestExceptionFilter turns this exception back into a 400.
        var exception = Assert.ThrowsAsync<HttpRequestException>(
            () => controller.GetCanceledMatches(null, null, "tampered", 25));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
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

    private class CapturingHandler(string responseBody, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            });
        }
    }
}
