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
        (nameof(CommercialEventsController.GetEvents), "GET", "events"),
        (nameof(CommercialEventsController.GetEvent), "GET", "events/{eventId}"),
        (nameof(CommercialEventsController.CreateEvent), "POST", "events"),
        (nameof(CommercialEventsController.UpdateEvent), "PUT", "events/{eventId}"),
        (nameof(CommercialEventsController.MoveEvent), "POST", "events/{eventId}/move"),
        (nameof(CommercialEventsController.CloseEvent), "POST", "events/{eventId}/close"),
        (nameof(CommercialEventsController.SuspendEvent), "POST", "events/{eventId}/suspend"),
        (nameof(CommercialEventsController.UnsuspendEvent), "POST", "events/{eventId}/unsuspend"),
        (nameof(CommercialEventsController.GetEventPeople), "GET", "events/{eventId}/people"),
        (nameof(CommercialEventsController.AddEventPerson), "PUT", "events/{eventId}/people/{targetBattleTag}"),
        (nameof(CommercialEventsController.RemoveEventPerson), "DELETE", "events/{eventId}/people/{targetBattleTag}"),
        (nameof(CommercialEventsController.GetEventGames), "GET", "events/{eventId}/games"),
        (nameof(CommercialEventsController.GetActiveGames), "GET", "games/active"),
        (nameof(CommercialEventsController.TerminateGame), "POST", "games/{matchId}/terminate"),
        (nameof(CommercialEventsController.GetAudit), "GET", "audit"),
        (nameof(CommercialEventsController.GetRoleHints), "POST", "roles/lookup"),
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
    [TestCase(typeof(CommercialEventCreateRequest))]
    [TestCase(typeof(CommercialEventUpdateRequest))]
    [TestCase(typeof(CommercialEventMoveRequest))]
    [TestCase(typeof(CommercialEventSuspendRequest))]
    [TestCase(typeof(CommercialEventPersonRequest))]
    [TestCase(typeof(CommercialEventRoleLookupRequest))]
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
        Assert.That(JsonSerializer.Serialize(((OkObjectResult)result).Value, WebJson), Does.Contain("\"resetsAt\":\"2026-10-15T00:00:00Z\""));
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
        AssertInvalidSegment(await controller.AddAllocationMember(value, "Grubby#1234", "Admin#1"), "allocationId");
        AssertInvalidSegment(await controller.RemoveAllocationMember("alloc-1", value, "Admin#1"), "battleTag");
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

    [Test]
    public void BodylessWritesBindNoRequestBody()
    {
        // Index P34: the website sends no body on these writes, so binding one would answer an empty request with 400/415.
        string[] bodyless =
        [
            nameof(CommercialEventsController.AddAllocationMember),
            nameof(CommercialEventsController.RemoveAllocationMember),
            nameof(CommercialEventsController.EndAllocation),
            nameof(CommercialEventsController.DeleteAllocation),
            nameof(CommercialEventsController.CloseEvent),
            nameof(CommercialEventsController.UnsuspendEvent),
            nameof(CommercialEventsController.RemoveEventPerson),
            nameof(CommercialEventsController.TerminateGame),
        ];
        foreach (var name in bodyless)
        {
            var parameters = typeof(CommercialEventsController).GetMethod(name)!.GetParameters();
            Assert.That(parameters.Where(parameter => parameter.GetCustomAttribute<FromBodyAttribute>() != null), Is.Empty, name);
            // [ApiController] infers [FromBody] for complex types, so every parameter must be a string.
            Assert.That(parameters.Select(parameter => parameter.ParameterType), Is.All.EqualTo(typeof(string)), name);
        }
    }

    [Test]
    public void RoleHintsBindOnlyTheLookupBody()
    {
        // Index P41: a read with a JSON body; the filter's acting `battleTag` argument is not part of the action.
        var parameter = typeof(CommercialEventsController).GetMethod(nameof(CommercialEventsController.GetRoleHints))!.GetParameters().Single();

        Assert.That(parameter.ParameterType, Is.EqualTo(typeof(CommercialEventRoleLookupRequest)));
        Assert.That(parameter.GetCustomAttribute<FromBodyAttribute>(), Is.Not.Null);
    }

    [Test]
    public async Task GetEventsForwardsTheFilters()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, CommercialEventsTestJson.Events);

        var result = await CreateController(handler).GetEvents("open", "active", "alloc-1", "EV-7K3M");

        var events = (List<CommercialEventDto>)((OkObjectResult)result).Value!;
        Assert.That(events.Single().Id, Is.EqualTo("EV-7K3M"));
        AssertForwarded(handler, 0, HttpMethod.Get, "/admin/commercial-events/events", "?status=open&phase=active&allocationId=alloc-1&q=EV-7K3M");
    }

    [Test]
    public async Task EventResponsesSerializeAsCamelCaseJsonForTheWebsite()
    {
        var result = await CreateController(new StubMatchmakingHandler(HttpStatusCode.OK, CommercialEventsTestJson.EventDetail)).GetEvent("EV-7K3M");

        var json = JToken.Parse(JsonSerializer.Serialize(((OkObjectResult)result).Value, WebJson));

        Assert.That(json["prizePoolUsd"]!.Value<int>(), Is.EqualTo(500));
        Assert.That(json["suspensionMessage"]!.Value<string>(), Is.EqualTo("Paused while we review the results"));
        Assert.That(json["organizers"]![0]!.Value<string>(), Is.EqualTo("Organizer#1234"));
        Assert.That(json["hosts"]![0]!["battleTag"]!.Value<string>(), Is.EqualTo("Host#3456"));
        // Absent optional fields may come out as null; the website treats null and absent the same.
        Assert.That(json["phase"]!.Type, Is.EqualTo(JTokenType.Null));
    }

    [Test]
    public async Task GetEventForwardsTheId()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, CommercialEventsTestJson.EventDetail);

        var result = await CreateController(handler).GetEvent("EV-7K3M");

        Assert.That(((OkObjectResult)result).Value, Is.InstanceOf<CommercialEventDetailDto>());
        AssertForwarded(handler, 0, HttpMethod.Get, "/admin/commercial-events/events/EV-7K3M");
    }

    [Test]
    public async Task CreateEventAnswers201AndForwardsTheActingAdmin()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.Created, CommercialEventsTestJson.EventDetail);

        var result = await CreateController(handler).CreateEvent(new CommercialEventCreateRequest { AllocationId = "alloc-1", Name = "Friday Showmatch" }, "Admin#1");

        Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(201));
        Assert.That(((ObjectResult)result).Value, Is.InstanceOf<CommercialEventDetailDto>());
        AssertForwarded(handler, 0, HttpMethod.Post, "/admin/commercial-events/events");
        var body = JObject.Parse(handler.RequestBodies[0]);
        Assert.That(body["allocationId"]!.Value<string>(), Is.EqualTo("alloc-1"));
        Assert.That(body["name"]!.Value<string>(), Is.EqualTo("Friday Showmatch"));
        Assert.That(body["actingBattleTag"]!.Value<string>(), Is.EqualTo("Admin#1"));
    }

    [Test]
    public async Task UpdateEventForwardsTheIdAndActingAdmin()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, CommercialEventsTestJson.EventDetail);

        var result = await CreateController(handler).UpdateEvent("EV-7K3M", new CommercialEventUpdateRequest { MaxGames = 12 }, "Admin#1");

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        AssertForwarded(handler, 0, HttpMethod.Put, "/admin/commercial-events/events/EV-7K3M");
        Assert.That(handler.RequestBodies[0], Is.EqualTo("""{"maxGames":12,"actingBattleTag":"Admin#1"}"""));
    }

    [Test]
    public async Task MoveEventForwardsTheTargetAllocation()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, CommercialEventsTestJson.EventDetail);

        var result = await CreateController(handler).MoveEvent("EV-7K3M", new CommercialEventMoveRequest { AllocationId = "alloc-2" }, "Admin#1");

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        AssertForwarded(handler, 0, HttpMethod.Post, "/admin/commercial-events/events/EV-7K3M/move");
        Assert.That(handler.RequestBodies[0], Is.EqualTo("""{"allocationId":"alloc-2","actingBattleTag":"Admin#1"}"""));
    }

    [Test]
    public async Task CloseAndUnsuspendForwardTheActingAdmin()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, CommercialEventsTestJson.EventDetail);
        var controller = CreateController(handler);

        var close = await controller.CloseEvent("EV-7K3M", "Admin#1");
        var unsuspend = await controller.UnsuspendEvent("EV-7K3M", "Admin#1");

        Assert.That(close, Is.InstanceOf<OkObjectResult>());
        Assert.That(unsuspend, Is.InstanceOf<OkObjectResult>());
        AssertForwarded(handler, 0, HttpMethod.Post, "/admin/commercial-events/events/EV-7K3M/close");
        AssertForwarded(handler, 1, HttpMethod.Post, "/admin/commercial-events/events/EV-7K3M/unsuspend");
        Assert.That(handler.RequestBodies, Is.EqualTo(new[] { ActingOnlyBody, ActingOnlyBody }));
    }

    [Test]
    public async Task SuspendEventForwardsTheMessage()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, CommercialEventsTestJson.EventDetail);

        var result = await CreateController(handler).SuspendEvent("EV-7K3M", new CommercialEventSuspendRequest { SuspensionMessage = "Paused" }, "Admin#1");

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        AssertForwarded(handler, 0, HttpMethod.Post, "/admin/commercial-events/events/EV-7K3M/suspend");
        Assert.That(handler.RequestBodies[0], Is.EqualTo("""{"suspensionMessage":"Paused","actingBattleTag":"Admin#1"}"""));
    }

    [Test]
    public async Task GetEventPeopleForwardsTheId()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, CommercialEventsTestJson.People);

        var result = await CreateController(handler).GetEventPeople("EV-7K3M");

        var people = (CommercialEventPeopleDto)((OkObjectResult)result).Value!;
        Assert.That(people.Delegates.Single().BattleTag, Is.EqualTo("Delegate#2345"));
        AssertForwarded(handler, 0, HttpMethod.Get, "/admin/commercial-events/events/EV-7K3M/people");
    }

    [Test]
    public async Task EventPersonRoutesForwardTheEncodedTargetRoleAndActingAdmin()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, CommercialEventsTestJson.People);
        var controller = CreateController(handler);

        var add = await controller.AddEventPerson("EV-7K3M", "Grubby#1234", new CommercialEventPersonRequest { Role = "delegate" }, "Admin#1");
        var remove = await controller.RemoveEventPerson("EV-7K3M", "Grubby#1234", "Admin#1");

        Assert.That(add, Is.InstanceOf<OkObjectResult>());
        Assert.That(remove, Is.InstanceOf<OkObjectResult>());
        AssertForwarded(handler, 0, HttpMethod.Put, "/admin/commercial-events/events/EV-7K3M/people/Grubby%231234");
        AssertForwarded(handler, 1, HttpMethod.Delete, "/admin/commercial-events/events/EV-7K3M/people/Grubby%231234");
        Assert.That(handler.RequestBodies[0], Is.EqualTo("""{"role":"delegate","actingBattleTag":"Admin#1"}"""));
        Assert.That(handler.RequestBodies[1], Is.EqualTo(ActingOnlyBody));
    }

    [Test]
    public async Task GetEventGamesForwardsCursorAndLimit()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, CommercialEventsTestJson.GamesPage);

        var result = await CreateController(handler).GetEventGames("EV-7K3M", "c1", "50");

        var page = (CommercialEventGamesPageDto)((OkObjectResult)result).Value!;
        Assert.That(page.Games.Single().MatchId, Is.EqualTo("m-1"));
        AssertForwarded(handler, 0, HttpMethod.Get, "/admin/commercial-events/events/EV-7K3M/games", "?cursor=c1&limit=50");
    }

    [Test]
    public async Task GetActiveGamesReturnsTheMatchmakingList()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, CommercialEventsTestJson.ActiveGames);

        var result = await CreateController(handler).GetActiveGames();

        var games = (List<CommercialEventActiveGameDto>)((OkObjectResult)result).Value!;
        Assert.That(games.Single().ViewerCount, Is.EqualTo(17));
        AssertForwarded(handler, 0, HttpMethod.Get, "/admin/commercial-events/games/active");
    }

    [Test]
    public async Task TerminateGameAnswersNoContentAndForwardsTheActingAdmin()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.NoContent);

        var result = await CreateController(handler).TerminateGame("m-2", "Admin#1");

        Assert.That(result, Is.InstanceOf<NoContentResult>());
        AssertForwarded(handler, 0, HttpMethod.Post, "/admin/commercial-events/games/m-2/terminate");
        Assert.That(handler.RequestBodies[0], Is.EqualTo(ActingOnlyBody));
    }

    [Test]
    public async Task GetAuditForwardsTheScope()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, CommercialEventsTestJson.Audit);

        var result = await CreateController(handler).GetAudit("EV-7K3M", null);

        var entries = (List<CommercialEventAuditEntryDto>)((OkObjectResult)result).Value!;
        Assert.That(entries.Single().Action, Is.EqualTo("event-updated"));
        AssertForwarded(handler, 0, HttpMethod.Get, "/admin/commercial-events/audit", "?eventId=EV-7K3M");
    }

    [Test]
    public async Task GetRoleHintsPostsEveryBattleTagWithoutActingBattleTag()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, CommercialEventsTestJson.RoleHints);

        var result = await CreateController(handler).GetRoleHints(new CommercialEventRoleLookupRequest { BattleTags = ["Grubby#1234", "Moon#5678"] });

        var hints = (List<CommercialEventRoleHintsDto>)((OkObjectResult)result).Value!;
        Assert.That(hints, Has.Count.EqualTo(2));
        AssertForwarded(handler, 0, HttpMethod.Post, "/admin/commercial-events/roles/lookup");
        Assert.That(handler.RequestBodies[0], Is.EqualTo("""{"battleTags":["Grubby#1234","Moon#5678"]}"""));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase(".")]
    [TestCase("..")]
    public async Task InvalidEventAndGamePathValuesAreRejectedWithoutProxying(string value)
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, CommercialEventsTestJson.EventDetail);
        var controller = CreateController(handler);

        AssertInvalidSegment(await controller.GetEvent(value), "eventId");
        AssertInvalidSegment(await controller.UpdateEvent(value, new CommercialEventUpdateRequest(), "Admin#1"), "eventId");
        AssertInvalidSegment(await controller.MoveEvent(value, new CommercialEventMoveRequest(), "Admin#1"), "eventId");
        AssertInvalidSegment(await controller.CloseEvent(value, "Admin#1"), "eventId");
        AssertInvalidSegment(await controller.SuspendEvent(value, new CommercialEventSuspendRequest(), "Admin#1"), "eventId");
        AssertInvalidSegment(await controller.UnsuspendEvent(value, "Admin#1"), "eventId");
        AssertInvalidSegment(await controller.GetEventPeople(value), "eventId");
        AssertInvalidSegment(await controller.AddEventPerson("EV-7K3M", value, new CommercialEventPersonRequest(), "Admin#1"), "battleTag");
        AssertInvalidSegment(await controller.AddEventPerson(value, "Grubby#1234", new CommercialEventPersonRequest(), "Admin#1"), "eventId");
        AssertInvalidSegment(await controller.RemoveEventPerson(value, "Grubby#1234", "Admin#1"), "eventId");
        AssertInvalidSegment(await controller.RemoveEventPerson("EV-7K3M", value, "Admin#1"), "battleTag");
        AssertInvalidSegment(await controller.GetEventGames(value, null, null), "eventId");
        AssertInvalidSegment(await controller.TerminateGame(value, "Admin#1"), "matchId");
        Assert.That(handler.Requests, Is.Empty);
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
