using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using W3C.Contracts.Admin.Permission;
using W3C.Domain.MatchmakingService;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Admin;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using W3ChampionsStatisticService.WebApi.ExceptionFilters;

namespace WC3ChampionsStatisticService.Tests.Admin;

[TestFixture]
public class CommercialEventsControllerTests
{
    private const string ActingOnlyBody = """{"actingBattleTag":"Admin#1"}""";

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    // C-E4 route table. Every public action must appear here exactly once.
    private static readonly (string Action, string Method, string Template)[] ContractRoutes =
    [
        (nameof(CommercialEventsController.GetAllocations), "GET", "allocations"),
        (nameof(CommercialEventsController.CreateAllocation), "POST", "allocations"),
        (nameof(CommercialEventsController.UpdateAllocation), "PUT", "allocations/{allocationId}"),
        (nameof(CommercialEventsController.AddAllocationMember), "PUT", "allocations/{allocationId}/members/{targetBattleTag}"),
        (nameof(CommercialEventsController.RemoveAllocationMember), "DELETE", "allocations/{allocationId}/members/{targetBattleTag}"),
        (nameof(CommercialEventsController.EndAllocation), "POST", "allocations/{allocationId}/end"),
        (nameof(CommercialEventsController.DeleteAllocation), "DELETE", "allocations/{allocationId}"),
        (nameof(CommercialEventsController.GetAllocationPeriods), "GET", "allocations/{allocationId}/periods"),
    ];

    private static IEnumerable<TestCaseData> ContractRouteCases() =>
        ContractRoutes.Select(route => new TestCaseData(route.Action, route.Method, route.Template).SetName($"Route_{route.Action}"));

    [Test]
    public void ControllerIsRoutedUnderAdminCommercialEventsAndMapsMatchmakingErrors()
    {
        var type = typeof(CommercialEventsController);

        Assert.That(type.GetCustomAttribute<ApiControllerAttribute>(), Is.Not.Null);
        Assert.That(type.GetCustomAttribute<RouteAttribute>()!.Template, Is.EqualTo("api/admin/commercial-events"));
        Assert.That(type.GetCustomAttribute<MatchmakingPassthroughExceptionFilter>(), Is.Not.Null);
    }

    [Test]
    public void EveryActionRequiresCommercialLicensePermission()
    {
        Assert.That(Actions(), Is.Not.Empty);
        foreach (var action in Actions())
        {
            var attribute = action.GetCustomAttribute<BearerHasPermissionFilter>();
            Assert.That(attribute, Is.Not.Null, action.Name);
            Assert.That(attribute!.Permission, Is.EqualTo(EPermission.CommercialLicense), action.Name);
        }
    }

    [Test]
    public void EveryActionHasExactlyOneContractRoute()
    {
        Assert.That(Actions().Select(action => action.Name), Is.EquivalentTo(ContractRoutes.Select(route => route.Action)));
    }

    [TestCaseSource(nameof(ContractRouteCases))]
    public void ActionUsesItsContractRoute(string action, string httpMethod, string template)
    {
        var attribute = typeof(CommercialEventsController).GetMethod(action)!.GetCustomAttribute<HttpMethodAttribute>();

        Assert.That(attribute, Is.Not.Null);
        Assert.That(attribute!.HttpMethods, Is.EqualTo(new[] { httpMethod }));
        Assert.That(attribute.Template, Is.EqualTo(template));
    }

    [Test]
    public void OnlyTheFilterInjectedArgumentIsNamedBattleTag()
    {
        foreach (var action in Actions())
        {
            Assert.That(action.GetCustomAttribute<HttpMethodAttribute>()!.Template, Does.Not.Contain("{battleTag}"), action.Name);

            var battleTag = action.GetParameters().SingleOrDefault(parameter => parameter.Name == "battleTag");
            if (battleTag == null) continue;
            Assert.That(battleTag.GetCustomAttribute<NoTraceAttribute>(), Is.Not.Null, action.Name);
            Assert.That(battleTag.GetCustomAttributes().OfType<IBindingSourceMetadata>(), Is.Empty, action.Name);
        }
    }

    [TestCase(typeof(CommercialEventAllocationRequest))]
    public void RequestBodiesCannotCarryAnActingBattleTag(Type requestType)
    {
        Assert.That(requestType.GetProperty("ActingBattleTag"), Is.Null);
    }

    [Test]
    public async Task GetAllocationsReturnsTheMatchmakingList()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, CommercialEventsTestJson.Allocations);

        var result = await CreateController(handler).GetAllocations();

        var allocations = (List<CommercialEventAllocationDto>)((OkObjectResult)result).Value!;
        Assert.That(allocations.Single().Id, Is.EqualTo("alloc-1"));
        AssertForwarded(handler, 0, HttpMethod.Get, "/admin/commercial-events/allocations");
    }

    [Test]
    public async Task AllocationResponsesSerializeAsCamelCaseJsonForTheWebsite()
    {
        var result = await CreateController(new StubMatchmakingHandler(HttpStatusCode.OK, CommercialEventsTestJson.Allocations)).GetAllocations();

        var json = JToken.Parse(JsonSerializer.Serialize(((OkObjectResult)result).Value, WebJson))[0]!;

        Assert.That(json["gamesPerPeriod"]!.Value<int>(), Is.EqualTo(20));
        Assert.That(json["allowEventCreation"]!.Value<bool>(), Is.True);
        Assert.That(json["members"]![0]!["battleTag"]!.Value<string>(), Is.EqualTo("Organizer#1234"));
        Assert.That(json["currentPeriod"]!["available"]!.Value<int>(), Is.EqualTo(14));
        Assert.That(json["resetsAt"]!.Value<DateTime>(), Is.EqualTo(new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Test]
    public async Task CreateAllocationAnswers201AndForwardsTheActingAdmin()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.Created, CommercialEventsTestJson.Allocation);

        var result = await CreateController(handler).CreateAllocation(new CommercialEventAllocationRequest { Name = "Weekly showmatches", GamesPerPeriod = 20 }, "Admin#1");

        Assert.That(result, Is.InstanceOf<ObjectResult>());
        Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(201));
        Assert.That(((ObjectResult)result).Value, Is.InstanceOf<CommercialEventAllocationDto>());
        AssertForwarded(handler, 0, HttpMethod.Post, "/admin/commercial-events/allocations");
        Assert.That(handler.RequestBodies[0], Is.EqualTo("""{"name":"Weekly showmatches","gamesPerPeriod":20,"actingBattleTag":"Admin#1"}"""));
    }

    [Test]
    public async Task ActingBattleTagInTheWebsiteBodyIsIgnored()
    {
        var body = JsonSerializer.Deserialize<CommercialEventAllocationRequest>("""{"name":"x","actingBattleTag":"Spoof#1"}""", WebJson);
        var handler = new StubMatchmakingHandler(HttpStatusCode.Created, CommercialEventsTestJson.Allocation);

        await CreateController(handler).CreateAllocation(body, "Admin#1");

        Assert.That(JObject.Parse(handler.RequestBodies[0])["actingBattleTag"]!.Value<string>(), Is.EqualTo("Admin#1"));
    }

    [Test]
    public async Task UpdateAllocationForwardsTheIdAndActingAdmin()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, CommercialEventsTestJson.Allocation);

        var result = await CreateController(handler).UpdateAllocation("alloc-1", new CommercialEventAllocationRequest { GamesPerPeriod = 25 }, "Admin#1");

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        AssertForwarded(handler, 0, HttpMethod.Put, "/admin/commercial-events/allocations/alloc-1");
        Assert.That(handler.RequestBodies[0], Is.EqualTo("""{"gamesPerPeriod":25,"actingBattleTag":"Admin#1"}"""));
    }

    [Test]
    public async Task AllocationMemberRoutesForwardTheEncodedTargetAndActingAdmin()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, CommercialEventsTestJson.Allocation);
        var controller = CreateController(handler);

        var add = await controller.AddAllocationMember("alloc-1", "Grubby#1234", "Admin#1");
        var remove = await controller.RemoveAllocationMember("alloc-1", "Grubby#1234", "Admin#1");

        Assert.That(add, Is.InstanceOf<OkObjectResult>());
        Assert.That(remove, Is.InstanceOf<OkObjectResult>());
        AssertForwarded(handler, 0, HttpMethod.Put, "/admin/commercial-events/allocations/alloc-1/members/Grubby%231234");
        AssertForwarded(handler, 1, HttpMethod.Delete, "/admin/commercial-events/allocations/alloc-1/members/Grubby%231234");
        Assert.That(handler.RequestBodies, Is.EqualTo(new[] { ActingOnlyBody, ActingOnlyBody }));
    }

    [Test]
    public async Task EndAllocationForwardsTheActingAdmin()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, CommercialEventsTestJson.Allocation);

        var result = await CreateController(handler).EndAllocation("alloc-1", "Admin#1");

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        AssertForwarded(handler, 0, HttpMethod.Post, "/admin/commercial-events/allocations/alloc-1/end");
        Assert.That(handler.RequestBodies[0], Is.EqualTo(ActingOnlyBody));
    }

    [Test]
    public async Task DeleteAllocationAnswersNoContent()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.NoContent);

        var result = await CreateController(handler).DeleteAllocation("alloc-1", "Admin#1");

        Assert.That(result, Is.InstanceOf<NoContentResult>());
        AssertForwarded(handler, 0, HttpMethod.Delete, "/admin/commercial-events/allocations/alloc-1");
        Assert.That(handler.RequestBodies[0], Is.EqualTo(ActingOnlyBody));
    }

    [Test]
    public async Task GetAllocationPeriodsReturnsTheMatchmakingList()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, CommercialEventsTestJson.Periods);

        var result = await CreateController(handler).GetAllocationPeriods("alloc-1");

        var periods = (List<CommercialEventPeriodUsageDto>)((OkObjectResult)result).Value!;
        Assert.That(periods.Single().Warning, Is.EqualTo("high"));
        AssertForwarded(handler, 0, HttpMethod.Get, "/admin/commercial-events/allocations/alloc-1/periods");
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase(".")]
    [TestCase("..")]
    public async Task InvalidAllocationPathValuesAreRejectedWithoutProxying(string value)
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, CommercialEventsTestJson.Allocation);
        var controller = CreateController(handler);

        AssertInvalidSegment(await controller.UpdateAllocation(value, new CommercialEventAllocationRequest(), "Admin#1"), "allocationId");
        AssertInvalidSegment(await controller.AddAllocationMember("alloc-1", value, "Admin#1"), "battleTag");
        AssertInvalidSegment(await controller.RemoveAllocationMember(value, "Grubby#1234", "Admin#1"), "allocationId");
        AssertInvalidSegment(await controller.EndAllocation(value, "Admin#1"), "allocationId");
        AssertInvalidSegment(await controller.DeleteAllocation(value, "Admin#1"), "allocationId");
        AssertInvalidSegment(await controller.GetAllocationPeriods(value), "allocationId");
        Assert.That(handler.Requests, Is.Empty);
    }

    [Test]
    public void MatchmakingErrorsReachTheWebsiteVerbatim()
    {
        var controller = CreateController(new StubMatchmakingHandler(HttpStatusCode.Conflict, CommercialEventsTestJson.AllocationInUseError));

        var ex = Assert.ThrowsAsync<MatchmakingPassthroughException>(async () => await controller.DeleteAllocation("alloc-1", "Admin#1"));

        var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        var context = new ExceptionContext(actionContext, new List<IFilterMetadata>()) { Exception = ex! };
        new MatchmakingPassthroughExceptionFilter().OnException(context);
        var result = (ContentResult)context.Result!;
        Assert.That(result.StatusCode, Is.EqualTo(409));
        Assert.That(result.Content, Is.EqualTo(CommercialEventsTestJson.AllocationInUseError));
    }

    private static void AssertForwarded(StubMatchmakingHandler handler, int index, HttpMethod method, string path, string query = "")
    {
        var request = handler.Requests[index];
        Assert.That(request.Method, Is.EqualTo(method));
        Assert.That(request.Headers.Contains("x-admin-secret"), Is.True);
        Assert.That(request.RequestUri!.AbsolutePath, Is.EqualTo(path));
        Assert.That(request.RequestUri.Query, Is.EqualTo(query));
    }

    private static void AssertInvalidSegment(IActionResult result, string field)
    {
        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>(), field);
        var body = JObject.FromObject(((BadRequestObjectResult)result).Value!);
        Assert.That(body["error"]!.Value<string>(), Is.EqualTo($"{field}: invalid"));
        Assert.That(body["code"]!.Value<string>(), Is.EqualTo("INVALID_REQUEST"));
        Assert.That(body["field"]!.Value<string>(), Is.EqualTo(field));
    }

    private static MethodInfo[] Actions() =>
        typeof(CommercialEventsController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

    private static CommercialEventsController CreateController(StubMatchmakingHandler handler) =>
        new(new MatchmakingServiceClient(new StubHttpClientFactory(new HttpClient(handler))));
}
