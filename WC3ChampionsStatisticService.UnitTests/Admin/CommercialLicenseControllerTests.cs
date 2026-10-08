using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using W3C.Contracts.Admin.Permission;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Admin;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace WC3ChampionsStatisticService.Tests.Admin;

[TestFixture]
public class CommercialLicenseControllerTests
{
    private const string TaggedPlayerJson =
        "{\"battleTag\":\"Grubby#1234\",\"note\":\"n\",\"notify\":true,\"createdBy\":\"Admin#1\",\"createdAt\":\"2026-10-08T10:00:00.000Z\",\"updatedBy\":\"Admin#1\",\"updatedAt\":\"2026-10-08T10:00:00.000Z\"}";

    [Test]
    public void AllEndpointsRequireCommercialLicensePermission()
    {
        AssertCommercialLicensePermission(nameof(CommercialLicenseController.GetTaggedPlayers));
        AssertCommercialLicensePermission(nameof(CommercialLicenseController.PutTaggedPlayer));
        AssertCommercialLicensePermission(nameof(CommercialLicenseController.DeleteTaggedPlayer));
    }

    [Test]
    public void ControllerIsRoutedUnderAdminCommercialLicense()
    {
        var route = typeof(CommercialLicenseController).GetCustomAttribute<RouteAttribute>();

        Assert.That(route, Is.Not.Null);
        Assert.That(route!.Template, Is.EqualTo("api/admin/commercial-license"));
    }

    [Test]
    public async Task GetTaggedPlayersReturnsMatchmakingList()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, $"[{TaggedPlayerJson}]");
        var controller = CreateController(handler);

        var result = await controller.GetTaggedPlayers();

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var players = (System.Collections.Generic.List<CommercialLicenseTaggedPlayerDto>)((OkObjectResult)result).Value!;
        Assert.That(players, Has.Count.EqualTo(1));
        Assert.That(players[0].battleTag, Is.EqualTo("Grubby#1234"));
        Assert.That(handler.Requests[0].Headers.Contains("x-admin-secret"), Is.True);
    }

    [Test]
    public async Task PutTaggedPlayerForwardsEncodedTargetAndInjectsActingBattleTag()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, TaggedPlayerJson);
        var controller = CreateController(handler);

        var result = await controller.PutTaggedPlayer("Grubby#1234", new CommercialLicenseTaggedPlayerRequest
        {
            note = "Streams for money",
            notify = true,
        }, "Admin#1");

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        Assert.That(handler.Requests, Has.Count.EqualTo(1));
        Assert.That(handler.Requests[0].Method, Is.EqualTo(HttpMethod.Put));
        Assert.That(handler.Requests[0].RequestUri!.AbsolutePath, Does.EndWith("/admin/commercial-license/tagged-players/Grubby%231234"));

        var body = JObject.Parse(handler.RequestBodies[0]);
        Assert.That(body["note"]!.Value<string>(), Is.EqualTo("Streams for money"));
        Assert.That(body["notify"]!.Value<bool>(), Is.True);
        Assert.That(body["actingBattleTag"]!.Value<string>(), Is.EqualTo("Admin#1"));
    }

    [Test]
    public async Task PutTaggedPlayerOverwritesSpoofedActingBattleTagFromBody()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, TaggedPlayerJson);
        var controller = CreateController(handler);

        await controller.PutTaggedPlayer("Grubby#1234", new CommercialLicenseTaggedPlayerRequest
        {
            note = "n",
            notify = false,
            actingBattleTag = "Evil#666",
        }, "Admin#1");

        var body = JObject.Parse(handler.RequestBodies[0]);
        Assert.That(body["actingBattleTag"]!.Value<string>(), Is.EqualTo("Admin#1"));
    }

    [Test]
    public async Task PutTaggedPlayerCoercesMissingNoteToEmptyString()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, TaggedPlayerJson);
        var controller = CreateController(handler);

        await controller.PutTaggedPlayer("Grubby#1234", new CommercialLicenseTaggedPlayerRequest { notify = true }, "Admin#1");

        var body = JObject.Parse(handler.RequestBodies[0]);
        Assert.That(body["note"]!.Value<string>(), Is.EqualTo(""));
    }

    [Test]
    public async Task PutTaggedPlayerRejectsMissingBodyBeforeProxying()
    {
        var handler = new StubMatchmakingHandler();
        var controller = CreateController(handler);

        var result = await controller.PutTaggedPlayer("Grubby#1234", null, "Admin#1");

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        Assert.That(handler.Requests, Is.Empty);
    }

    [Test]
    public async Task DeleteTaggedPlayerReturnsNoContent()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.NoContent);
        var controller = CreateController(handler);

        var result = await controller.DeleteTaggedPlayer("Grubby#1234");

        Assert.That(result, Is.InstanceOf<NoContentResult>());
        Assert.That(handler.Requests[0].Method, Is.EqualTo(HttpMethod.Delete));
        Assert.That(handler.Requests[0].Headers.Contains("x-admin-secret"), Is.True);
        Assert.That(handler.Requests[0].RequestUri!.AbsolutePath, Does.EndWith("/admin/commercial-license/tagged-players/Grubby%231234"));
    }

    [Test]
    public void DeleteTaggedPlayerPropagatesNotFoundFromMatchmaking()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.NotFound);
        var controller = CreateController(handler);

        var ex = Assert.ThrowsAsync<HttpRequestException>(async () => await controller.DeleteTaggedPlayer("Nobody#1"));

        // HttpRequestExceptionFilter maps this exception to a 404 response.
        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    private static void AssertCommercialLicensePermission(string methodName)
    {
        var method = typeof(CommercialLicenseController).GetMethod(methodName)!;
        var attribute = method.GetCustomAttribute<BearerHasPermissionFilter>();

        Assert.That(attribute, Is.Not.Null, methodName);
        Assert.That(attribute!.Permission, Is.EqualTo(EPermission.CommercialLicense), methodName);
    }

    private static CommercialLicenseController CreateController(StubMatchmakingHandler handler) =>
        new(new MatchmakingServiceClient(new StubHttpClientFactory(new HttpClient(handler))));
}
