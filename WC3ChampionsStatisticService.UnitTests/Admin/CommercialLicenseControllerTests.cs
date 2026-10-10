using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using W3C.Contracts.Admin.Permission;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Admin;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using W3ChampionsStatisticService.WebApi.ExceptionFilters;

namespace WC3ChampionsStatisticService.Tests.Admin;

[TestFixture]
public class CommercialLicenseControllerTests
{
    private const string TaggedPlayerJson =
        "{\"battleTag\":\"Grubby#1234\",\"note\":\"n\",\"notify\":true,\"createdBy\":\"Admin#1\",\"createdAt\":\"2026-10-08T10:00:00.000Z\",\"updatedBy\":\"Admin#1\",\"updatedAt\":\"2026-10-08T10:00:00.000Z\",\"restrictions\":{\"asPlayer\":false,\"asObserver\":false,\"floTv\":\"none\"}}";

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
        var players = (List<CommercialLicenseTaggedPlayerDto>)((OkObjectResult)result).Value!;
        Assert.That(players, Has.Count.EqualTo(1));
        Assert.That(players[0].battleTag, Is.EqualTo("Grubby#1234"));
        Assert.That(players[0].Restrictions.FloTv, Is.EqualTo("none"));
        Assert.That(handler.Requests[0].Headers.Contains("x-admin-secret"), Is.True);
    }

    [Test]
    public async Task PutTaggedPlayerForwardsEncodedTargetAndInjectsActingBattleTag()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, TaggedPlayerJson);
        var controller = CreateController(handler);

        var result = await controller.PutTaggedPlayer("Grubby#1234", new CommercialLicenseTaggedPlayerBody
        {
            Note = "Streams for money",
            Notify = true,
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
    public async Task PutTaggedPlayerTakesActingBattleTagOnlyFromAuthenticatedParameter()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, TaggedPlayerJson);
        var controller = CreateController(handler);

        // The inbound body type cannot carry an acting battleTag, so a spoofed body value is never bound.
        Assert.That(typeof(CommercialLicenseTaggedPlayerBody).GetProperty("ActingBattleTag"), Is.Null);

        // BearerHasPermissionFilter overwrites the action argument "battleTag", which is not exercised here
        // because there is no JWT test infrastructure; the action just receives the filter's result.
        await controller.PutTaggedPlayer("Grubby#1234", new CommercialLicenseTaggedPlayerBody { Note = "n", Notify = false }, "Admin#1");

        var body = JObject.Parse(handler.RequestBodies[0]);
        Assert.That(body["actingBattleTag"]!.Value<string>(), Is.EqualTo("Admin#1"));
    }

    [Test]
    public async Task PutTaggedPlayerCoercesMissingNoteToEmptyString()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, TaggedPlayerJson);
        var controller = CreateController(handler);

        await controller.PutTaggedPlayer("Grubby#1234", new CommercialLicenseTaggedPlayerBody { Notify = true }, "Admin#1");

        var body = JObject.Parse(handler.RequestBodies[0]);
        Assert.That(body["note"]!.Value<string>(), Is.EqualTo(""));
    }

    [Test]
    public async Task PutTaggedPlayerForwardsRestrictions()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, TaggedPlayerJson);
        var controller = CreateController(handler);

        await controller.PutTaggedPlayer("Grubby#1234", new CommercialLicenseTaggedPlayerBody
        {
            Note = "n",
            Notify = true,
            Restrictions = new CommercialLicenseRestrictionsBody { AsPlayer = true, AsObserver = true, FloTv = "all" },
        }, "Admin#1");

        var restrictions = JObject.Parse(handler.RequestBodies[0])["restrictions"];
        Assert.That(restrictions, Is.Not.Null);
        Assert.That(restrictions!["asPlayer"]!.Value<bool>(), Is.True);
        Assert.That(restrictions["asObserver"]!.Value<bool>(), Is.True);
        Assert.That(restrictions["floTv"]!.Value<string>(), Is.EqualTo("all"));
    }

    [Test]
    public async Task PutTaggedPlayerOmitsRestrictionsWhenAbsent()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, TaggedPlayerJson);
        var controller = CreateController(handler);

        var result = await controller.PutTaggedPlayer("Grubby#1234", new CommercialLicenseTaggedPlayerBody { Note = "n", Notify = true }, "Admin#1");

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        Assert.That(JObject.Parse(handler.RequestBodies[0]).ContainsKey("restrictions"), Is.False);
    }

    [TestCase("none")]
    [TestCase("custom")]
    [TestCase("all")]
    public async Task PutTaggedPlayerAcceptsEachValidFloTv(string floTv)
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, TaggedPlayerJson);
        var controller = CreateController(handler);

        var result = await controller.PutTaggedPlayer("Grubby#1234", new CommercialLicenseTaggedPlayerBody
        {
            Note = "n",
            Notify = true,
            Restrictions = new CommercialLicenseRestrictionsBody { AsPlayer = false, AsObserver = false, FloTv = floTv },
        }, "Admin#1");

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        Assert.That(JObject.Parse(handler.RequestBodies[0])["restrictions"]!["floTv"]!.Value<string>(), Is.EqualTo(floTv));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("ALL")]
    [TestCase("everyone")]
    public async Task PutTaggedPlayerRejectsInvalidFloTvBeforeProxying(string floTv)
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, TaggedPlayerJson);
        var controller = CreateController(handler);

        var result = await controller.PutTaggedPlayer("Grubby#1234", new CommercialLicenseTaggedPlayerBody
        {
            Note = "n",
            Notify = true,
            Restrictions = new CommercialLicenseRestrictionsBody { AsPlayer = true, AsObserver = false, FloTv = floTv },
        }, "Admin#1");

        AssertBadRequestError(result, "restrictions.floTv must be one of none, custom, all");
        Assert.That(handler.Requests, Is.Empty);
    }

    [Test]
    public async Task PutTaggedPlayerRejectsMissingAsPlayerBeforeProxying()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, TaggedPlayerJson);
        var controller = CreateController(handler);

        var result = await controller.PutTaggedPlayer("Grubby#1234", new CommercialLicenseTaggedPlayerBody
        {
            Note = "n",
            Notify = true,
            Restrictions = new CommercialLicenseRestrictionsBody { AsObserver = false, FloTv = "none" },
        }, "Admin#1");

        AssertBadRequestError(result, "restrictions.asPlayer must be a boolean");
        Assert.That(handler.Requests, Is.Empty);
    }

    [Test]
    public async Task PutTaggedPlayerRejectsMissingAsObserverBeforeProxying()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, TaggedPlayerJson);
        var controller = CreateController(handler);

        var result = await controller.PutTaggedPlayer("Grubby#1234", new CommercialLicenseTaggedPlayerBody
        {
            Note = "n",
            Notify = true,
            Restrictions = new CommercialLicenseRestrictionsBody { AsPlayer = true, FloTv = "none" },
        }, "Admin#1");

        AssertBadRequestError(result, "restrictions.asObserver must be a boolean");
        Assert.That(handler.Requests, Is.Empty);
    }

    [TestCase("{\"note\":\"\",\"notify\":true,\"restrictions\":{\"floTv\":\"all\"}}", "restrictions.asPlayer must be a boolean")]
    [TestCase("{\"note\":\"\",\"notify\":true,\"restrictions\":{\"asPlayer\":true,\"floTv\":\"all\"}}", "restrictions.asObserver must be a boolean")]
    public async Task PutTaggedPlayerRejectsJsonBodyWithoutBooleanFlagsInsteadOfDefaultingToFalse(string json, string expectedError)
    {
        var body = JsonSerializer.Deserialize<CommercialLicenseTaggedPlayerBody>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, TaggedPlayerJson);
        var controller = CreateController(handler);

        var result = await controller.PutTaggedPlayer("Grubby#1234", body, "Admin#1");

        AssertBadRequestError(result, expectedError);
        Assert.That(handler.Requests, Is.Empty);
    }

    [Test]
    public async Task ResponsesSerializeRestrictionsAsCamelCaseJson()
    {
        var put = await CreateController(new StubMatchmakingHandler(HttpStatusCode.OK, TaggedPlayerJson))
            .PutTaggedPlayer("Grubby#1234", new CommercialLicenseTaggedPlayerBody { Note = "n", Notify = true }, "Admin#1");
        var get = await CreateController(new StubMatchmakingHandler(HttpStatusCode.OK, $"[{TaggedPlayerJson}]")).GetTaggedPlayers();

        var putJson = JsonSerializer.Serialize(((OkObjectResult)put).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var getJson = JsonSerializer.Serialize(((OkObjectResult)get).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        foreach (var restrictions in new[] { JToken.Parse(putJson)["restrictions"], JToken.Parse(getJson)[0]!["restrictions"] })
        {
            Assert.That(restrictions, Is.Not.Null);
            Assert.That(restrictions!["asPlayer"]!.Value<bool>(), Is.False);
            Assert.That(restrictions["asObserver"]!.Value<bool>(), Is.False);
            Assert.That(restrictions["floTv"]!.Value<string>(), Is.EqualTo("none"));
        }
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
    public async Task PutTaggedPlayerRejectsMissingNotifyBeforeProxying()
    {
        var handler = new StubMatchmakingHandler();
        var controller = CreateController(handler);

        var result = await controller.PutTaggedPlayer("Grubby#1234", new CommercialLicenseTaggedPlayerBody { Note = "n" }, "Admin#1");

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        Assert.That(JObject.FromObject(((BadRequestObjectResult)result).Value!)["error"]!.Value<string>(), Is.EqualTo("invalid_request"));
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

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase(".")]
    [TestCase("..")]
    public async Task PutAndDeleteRejectInvalidTargetBattleTagWithoutProxying(string target)
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, TaggedPlayerJson);
        var controller = CreateController(handler);

        var put = await controller.PutTaggedPlayer(target, new CommercialLicenseTaggedPlayerBody { Note = "n", Notify = true }, "Admin#1");
        var delete = await controller.DeleteTaggedPlayer(target);

        Assert.That(put, Is.InstanceOf<BadRequestObjectResult>());
        Assert.That(delete, Is.InstanceOf<BadRequestObjectResult>());
        Assert.That(handler.Requests, Is.Empty);
    }

    [TestCase(HttpStatusCode.NotFound, "", 404, null)]
    [TestCase(HttpStatusCode.BadRequest, "{\"error\":\"note too long\"}", 400, "note too long")]
    public void ExceptionFilterMapsMatchmakingErrorsToStatusAndErrorBody(HttpStatusCode mmStatus, string mmBody, int expectedStatus, string expectedMessage)
    {
        var controller = CreateController(new StubMatchmakingHandler(mmStatus, mmBody));
        var ex = Assert.ThrowsAsync<HttpRequestException>(async () => await controller.DeleteTaggedPlayer("Nobody#1"));

        var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        var context = new ExceptionContext(actionContext, new List<IFilterMetadata>()) { Exception = ex! };
        new HttpRequestExceptionFilter().OnException(context);

        Assert.That(context.ExceptionHandled, Is.True);
        var result = (ObjectResult)context.Result!;
        Assert.That(result.StatusCode, Is.EqualTo(expectedStatus));
        var error = ((ErrorResult)result.Value!).Error;
        if (expectedMessage != null)
            Assert.That(error, Is.EqualTo(expectedMessage));
        else
            Assert.That(error, Is.Not.Null.And.Not.Empty);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task PutTaggedPlayerForwardsCommercialEventNotice(bool commercialEventNotice)
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, TaggedPlayerJson);
        var controller = CreateController(handler);

        await controller.PutTaggedPlayer("Grubby#1234", new CommercialLicenseTaggedPlayerBody
        {
            Note = "n",
            Notify = true,
            CommercialEventNotice = commercialEventNotice,
        }, "Admin#1");

        Assert.That(JObject.Parse(handler.RequestBodies[0])["commercialEventNotice"]!.Value<bool>(), Is.EqualTo(commercialEventNotice));
    }

    [TestCase("{\"note\":\"n\",\"notify\":true}")]
    [TestCase("{\"note\":\"n\",\"notify\":true,\"commercialEventNotice\":null}")]
    public async Task PutTaggedPlayerOmitsCommercialEventNoticeWhenAbsentOrNull(string json)
    {
        var body = JsonSerializer.Deserialize<CommercialLicenseTaggedPlayerBody>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, TaggedPlayerJson);

        await CreateController(handler).PutTaggedPlayer("Grubby#1234", body, "Admin#1");

        Assert.That(JObject.Parse(handler.RequestBodies[0]).ContainsKey("commercialEventNotice"), Is.False);
    }

    [Test]
    public async Task ResponsesSerializeCommercialEventNoticeAsCamelCaseJson()
    {
        var json = TaggedPlayerJson.Replace("\"notify\":true,", "\"notify\":true,\"commercialEventNotice\":true,");
        var get = await CreateController(new StubMatchmakingHandler(HttpStatusCode.OK, $"[{json}]")).GetTaggedPlayers();

        var getJson = JsonSerializer.Serialize(((OkObjectResult)get).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.That(JToken.Parse(getJson)[0]!["commercialEventNotice"]!.Value<bool>(), Is.True);
    }

    private static void AssertBadRequestError(IActionResult result, string expectedError)
    {
        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        Assert.That(JObject.FromObject(((BadRequestObjectResult)result).Value!)["error"]!.Value<string>(), Is.EqualTo(expectedError));
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
