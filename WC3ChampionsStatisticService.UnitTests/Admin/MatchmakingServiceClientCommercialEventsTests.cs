using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using W3C.Domain.MatchmakingService;

namespace WC3ChampionsStatisticService.Tests.Admin;

[TestFixture]
public class MatchmakingServiceClientCommercialEventsTests
{
    private const string ActingOnlyBody = """{"actingBattleTag":"Admin#1"}""";

    private static readonly string ExpectedAdminSecret = AdminSecretTestEnvironment.Secret;

    private static MatchmakingServiceClient CreateClient(HttpMessageHandler handler) =>
        new(new StubHttpClientFactory(new HttpClient(handler)));

    private static DateTime Utc(int year, int month, int day, int hour = 0, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    [Test]
    public async Task GetAllocationsParsesDtosAndSendsAdminSecret()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, CommercialEventsTestJson.Allocations);

        var allocations = await CreateClient(handler).GetCommercialEventAllocations();

        AssertRequest(handler, 0, HttpMethod.Get, "/admin/commercial-events/allocations");
        var allocation = allocations.Single();
        Assert.That(allocation.Id, Is.EqualTo("alloc-1"));
        Assert.That(allocation.Name, Is.EqualTo("Weekly showmatches"));
        Assert.That(allocation.GamesPerPeriod, Is.EqualTo(20));
        Assert.That(allocation.Recurrence, Is.EqualTo("weekly"));
        Assert.That(allocation.StartsAt, Is.EqualTo(Utc(2026, 10, 1)));
        Assert.That(allocation.EndsAt, Is.EqualTo(Utc(2026, 12, 31)));
        Assert.That(allocation.AllowEventCreation, Is.True);
        Assert.That(allocation.AdminNote, Is.EqualTo(""));
        Assert.That(allocation.State, Is.EqualTo("active"));
        var member = allocation.Members.Single();
        Assert.That(member.BattleTag, Is.EqualTo("Organizer#1234"));
        Assert.That(member.AddedBy, Is.EqualTo("Admin#1"));
        Assert.That(member.AddedAt, Is.EqualTo(Utc(2026, 10, 1, 10)));
        var period = allocation.CurrentPeriod;
        Assert.That(period.PeriodId, Is.EqualTo("alloc-1:2026-10-08T00:00:00.000Z"));
        Assert.That(period.PeriodStart, Is.EqualTo(Utc(2026, 10, 8)));
        Assert.That(period.PeriodEnd, Is.EqualTo(Utc(2026, 10, 15)));
        Assert.That(period.Size, Is.EqualTo(20));
        Assert.That(period.Consumed, Is.EqualTo(5));
        Assert.That(period.Held, Is.EqualTo(1));
        Assert.That(period.Invalid, Is.EqualTo(2));
        Assert.That(period.Used, Is.EqualTo(6));
        Assert.That(period.Available, Is.EqualTo(14));
        Assert.That(period.Warning, Is.EqualTo("none"));
        Assert.That(allocation.ResetsAt, Is.EqualTo(Utc(2026, 10, 15)));
        Assert.That(allocation.CreatedBy, Is.EqualTo("Admin#1"));
        Assert.That(allocation.CreatedAt, Is.EqualTo(Utc(2026, 10, 1, 9)));
        Assert.That(allocation.UpdatedBy, Is.EqualTo("Admin#2"));
        Assert.That(allocation.UpdatedAt, Is.EqualTo(Utc(2026, 10, 2, 9)));
    }

    [Test]
    public async Task GetAllocationsReturnsEmptyListForEmptyBody()
    {
        var allocations = await CreateClient(new StubMatchmakingHandler(HttpStatusCode.OK, "")).GetCommercialEventAllocations();

        Assert.That(allocations, Is.Empty);
    }

    [Test]
    public async Task CreateAllocationPostsCamelCaseBodyWithActingBattleTagAndOmitsNulls()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.Created, CommercialEventsTestJson.Allocation);

        var allocation = await CreateClient(handler).CreateCommercialEventAllocation(new CommercialEventAllocationRequest
        {
            Name = "Weekly showmatches",
            GamesPerPeriod = 20,
            Recurrence = "weekly",
            StartsAt = Utc(2026, 10, 1),
            EndsAt = Utc(2026, 12, 31),
            AllowEventCreation = true,
        }, "Admin#1");

        AssertRequest(handler, 0, HttpMethod.Post, "/admin/commercial-events/allocations");
        var body = JObject.Parse(handler.RequestBodies[0]);
        Assert.That(body.Properties().Select(p => p.Name), Is.EquivalentTo(new[]
        {
            "name", "gamesPerPeriod", "recurrence", "startsAt", "endsAt", "allowEventCreation", "actingBattleTag",
        }));
        Assert.That(body["name"]!.Value<string>(), Is.EqualTo("Weekly showmatches"));
        Assert.That(body["gamesPerPeriod"]!.Value<int>(), Is.EqualTo(20));
        Assert.That(body["recurrence"]!.Value<string>(), Is.EqualTo("weekly"));
        Assert.That(body["allowEventCreation"]!.Value<bool>(), Is.True);
        Assert.That(body["actingBattleTag"]!.Value<string>(), Is.EqualTo("Admin#1"));
        // Exactly the Date.prototype.toISOString() format.
        Assert.That(handler.RequestBodies[0], Does.Contain("\"startsAt\":\"2026-10-01T00:00:00.000Z\""));
        Assert.That(handler.RequestBodies[0], Does.Contain("\"endsAt\":\"2026-12-31T00:00:00.000Z\""));
        Assert.That(allocation.Id, Is.EqualTo("alloc-1"));
    }

    [Test]
    public async Task UpdateAllocationSendsOnlyTheGivenFields()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, CommercialEventsTestJson.Allocation);

        await CreateClient(handler).UpdateCommercialEventAllocation("alloc-1", new CommercialEventAllocationRequest { GamesPerPeriod = 25 }, "Admin#1");

        AssertRequest(handler, 0, HttpMethod.Put, "/admin/commercial-events/allocations/alloc-1");
        Assert.That(handler.RequestBodies[0], Is.EqualTo("""{"gamesPerPeriod":25,"actingBattleTag":"Admin#1"}"""));
    }

    [Test]
    public async Task MemberRoutesEscapeTheBattleTagAndSendOnlyTheActingBattleTag()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, CommercialEventsTestJson.Allocation);
        var client = CreateClient(handler);

        var added = await client.AddCommercialEventAllocationMember("alloc-1", "Grubby#1234", "Admin#1");
        var removed = await client.RemoveCommercialEventAllocationMember("alloc-1", "Grubby#1234", "Admin#1");

        AssertRequest(handler, 0, HttpMethod.Put, "/admin/commercial-events/allocations/alloc-1/members/Grubby%231234");
        AssertRequest(handler, 1, HttpMethod.Delete, "/admin/commercial-events/allocations/alloc-1/members/Grubby%231234");
        Assert.That(handler.RequestBodies, Is.EqualTo(new[] { ActingOnlyBody, ActingOnlyBody }));
        // P34: body-less website writes still send a JSON body to matchmaking.
        Assert.That(handler.Requests.Select(request => request.Content!.Headers.ContentType!.MediaType), Is.All.EqualTo("application/json"));
        Assert.That(added.Id, Is.EqualTo("alloc-1"));
        Assert.That(removed.Id, Is.EqualTo("alloc-1"));
    }

    [Test]
    public async Task EndAllocationPostsTheActingBattleTag()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, CommercialEventsTestJson.Allocation);

        var allocation = await CreateClient(handler).EndCommercialEventAllocation("alloc-1", "Admin#1");

        AssertRequest(handler, 0, HttpMethod.Post, "/admin/commercial-events/allocations/alloc-1/end");
        Assert.That(handler.RequestBodies[0], Is.EqualTo(ActingOnlyBody));
        Assert.That(allocation.Id, Is.EqualTo("alloc-1"));
    }

    [Test]
    public async Task DeleteAllocationSendsTheActingBattleTagAndAcceptsNoContent()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.NoContent);

        await CreateClient(handler).DeleteCommercialEventAllocation("alloc-1", "Admin#1");

        AssertRequest(handler, 0, HttpMethod.Delete, "/admin/commercial-events/allocations/alloc-1");
        Assert.That(handler.RequestBodies[0], Is.EqualTo(ActingOnlyBody));
    }

    [Test]
    public async Task GetAllocationPeriodsParsesDtos()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, CommercialEventsTestJson.Periods);

        var periods = await CreateClient(handler).GetCommercialEventAllocationPeriods("alloc-1");

        AssertRequest(handler, 0, HttpMethod.Get, "/admin/commercial-events/allocations/alloc-1/periods");
        var period = periods.Single();
        Assert.That(period.PeriodStart, Is.EqualTo(Utc(2026, 10, 1)));
        Assert.That(period.Consumed, Is.EqualTo(18));
        Assert.That(period.Invalid, Is.EqualTo(3));
        Assert.That(period.Available, Is.EqualTo(2));
        Assert.That(period.Warning, Is.EqualTo("high"));
    }

    [Test]
    public void ClientErrorsThrowPassthroughWithTheVerbatimBody()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.Conflict, CommercialEventsTestJson.AllocationInUseError);

        var ex = Assert.ThrowsAsync<MatchmakingPassthroughException>(async () =>
            await CreateClient(handler).DeleteCommercialEventAllocation("alloc-1", "Admin#1"));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(ex.Body, Is.EqualTo(CommercialEventsTestJson.AllocationInUseError));
    }

    [Test]
    public void ServerErrorsThrowPassthroughWithTheStatus()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.InternalServerError, "{\"error\":\"connect ECONNREFUSED mongo-internal:27017\"}");

        var ex = Assert.ThrowsAsync<MatchmakingPassthroughException>(async () =>
            await CreateClient(handler).GetCommercialEventAllocations());

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
    }

    [Test]
    public void UnreachableMatchmakingThrowsBadGatewayPassthrough()
    {
        var handler = new ThrowingMatchmakingHandler(new HttpRequestException("Connection refused (matchmaking-service:3000)"));

        var ex = Assert.ThrowsAsync<MatchmakingPassthroughException>(async () =>
            await CreateClient(handler).GetCommercialEventAllocations());

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
        Assert.That(ex.Body, Is.Null);
        Assert.That(ex.InnerException, Is.InstanceOf<HttpRequestException>());
    }

    [Test]
    public void TimedOutMatchmakingThrowsGatewayTimeoutPassthrough()
    {
        var handler = new ThrowingMatchmakingHandler(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout."));

        var ex = Assert.ThrowsAsync<MatchmakingPassthroughException>(async () =>
            await CreateClient(handler).GetCommercialEventAllocations());

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.GatewayTimeout));
        Assert.That(ex.Body, Is.Null);
        Assert.That(ex.InnerException, Is.InstanceOf<TaskCanceledException>());
    }

    [Test]
    public void SuccessBodyThatDoesNotMatchTheDtoThrowsBadGatewayPassthrough()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, """{"unexpected":"object instead of array"}""");

        var ex = Assert.ThrowsAsync<MatchmakingPassthroughException>(async () =>
            await CreateClient(handler).GetCommercialEventAllocations());

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
        Assert.That(ex.Body, Is.Null);
        Assert.That(ex.InnerException, Is.InstanceOf<Newtonsoft.Json.JsonException>());
    }

    [Test]
    public async Task GetEventsForwardsOnlyNonEmptyFiltersEscaped()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, CommercialEventsTestJson.Events);

        var events = await CreateClient(handler).GetCommercialEvents("open", "", null, "Friday & co");

        AssertRequest(handler, 0, HttpMethod.Get, "/admin/commercial-events/events", "?status=open&q=Friday%20%26%20co");
        var commercialEvent = events.Single();
        Assert.That(commercialEvent.Id, Is.EqualTo("EV-7K3M"));
        Assert.That(commercialEvent.Name, Is.EqualTo("Friday Showmatch"));
        Assert.That(commercialEvent.Kind, Is.EqualTo("show-matches"));
        Assert.That(commercialEvent.PrizePoolUsd, Is.EqualTo(500));
        Assert.That(commercialEvent.StartsAt, Is.EqualTo(Utc(2026, 10, 9, 18)));
        Assert.That(commercialEvent.EndsAt, Is.EqualTo(Utc(2026, 10, 9, 23)));
        Assert.That(commercialEvent.MaxGames, Is.EqualTo(10));
        Assert.That(commercialEvent.AllocationId, Is.EqualTo("alloc-1"));
        Assert.That(commercialEvent.AllocationName, Is.EqualTo("Weekly showmatches"));
        Assert.That(commercialEvent.Status, Is.EqualTo("open"));
        Assert.That(commercialEvent.Phase, Is.EqualTo("active"));
        Assert.That(commercialEvent.Consumed, Is.EqualTo(3));
        Assert.That(commercialEvent.Held, Is.EqualTo(1));
        Assert.That(commercialEvent.Invalid, Is.EqualTo(2));
        Assert.That(commercialEvent.CreatedBy, Is.EqualTo("Organizer#1234"));
        Assert.That(commercialEvent.CreatedVia, Is.EqualTo("launcher"));
        Assert.That(commercialEvent.SuspensionMessage, Is.Null);
        Assert.That(commercialEvent.ClosedAt, Is.Null);
    }

    [Test]
    public async Task GetEventsWithoutFiltersSendsNoQuery()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, "[]");

        var events = await CreateClient(handler).GetCommercialEvents(null, null, null, null);

        AssertRequest(handler, 0, HttpMethod.Get, "/admin/commercial-events/events");
        Assert.That(events, Is.Empty);
    }

    [Test]
    public async Task GetEventParsesTheDetailWithPeopleAndSuspension()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, CommercialEventsTestJson.EventDetail);

        var detail = await CreateClient(handler).GetCommercialEvent("EV-7K3M");

        AssertRequest(handler, 0, HttpMethod.Get, "/admin/commercial-events/events/EV-7K3M");
        Assert.That(detail.Status, Is.EqualTo("suspended"));
        Assert.That(detail.Phase, Is.Null);
        Assert.That(detail.SuspensionMessage, Is.EqualTo("Paused while we review the results"));
        Assert.That(detail.SuspendedBy, Is.EqualTo("Admin#1"));
        Assert.That(detail.SuspendedAt, Is.EqualTo(Utc(2026, 10, 9, 19)));
        Assert.That(detail.AdminNote, Is.EqualTo("Reported on Discord"));
        Assert.That(detail.Organizers, Is.EqualTo(new[] { "Organizer#1234" }));
        Assert.That(detail.Delegates.Single().BattleTag, Is.EqualTo("Delegate#2345"));
        Assert.That(detail.Hosts.Single().BattleTag, Is.EqualTo("Host#3456"));
        Assert.That(detail.Hosts.Single().AddedBy, Is.EqualTo("Delegate#2345"));
    }

    [Test]
    public async Task CreateEventPostsTheBodyWithActingBattleTag()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.Created, CommercialEventsTestJson.EventDetail);

        var detail = await CreateClient(handler).CreateCommercialEvent(new CommercialEventCreateRequest
        {
            AllocationId = "alloc-1",
            Name = "Friday Showmatch",
            Kind = "show-matches",
            PrizePoolUsd = 500,
            StartsAt = Utc(2026, 10, 9, 18),
            EndsAt = Utc(2026, 10, 9, 23),
            MaxGames = 10,
        }, "Admin#1");

        AssertRequest(handler, 0, HttpMethod.Post, "/admin/commercial-events/events");
        var body = JObject.Parse(handler.RequestBodies[0]);
        Assert.That(body.Properties().Select(p => p.Name), Is.EquivalentTo(new[]
        {
            "allocationId", "name", "kind", "prizePoolUsd", "startsAt", "endsAt", "maxGames", "actingBattleTag",
        }));
        Assert.That(body["allocationId"]!.Value<string>(), Is.EqualTo("alloc-1"));
        Assert.That(body["prizePoolUsd"]!.Value<int>(), Is.EqualTo(500));
        Assert.That(body["actingBattleTag"]!.Value<string>(), Is.EqualTo("Admin#1"));
        Assert.That(handler.RequestBodies[0], Does.Contain("\"startsAt\":\"2026-10-09T18:00:00.000Z\""));
        Assert.That(detail.Id, Is.EqualTo("EV-7K3M"));
    }

    [Test]
    public async Task UpdateEventSendsOnlyTheGivenFields()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, CommercialEventsTestJson.EventDetail);

        await CreateClient(handler).UpdateCommercialEvent("EV-7K3M", new CommercialEventUpdateRequest { Name = "Friday Night Showmatch" }, "Admin#1");

        AssertRequest(handler, 0, HttpMethod.Put, "/admin/commercial-events/events/EV-7K3M");
        Assert.That(handler.RequestBodies[0], Is.EqualTo("""{"name":"Friday Night Showmatch","actingBattleTag":"Admin#1"}"""));
    }

    [Test]
    public async Task MoveEventPostsTheTargetAllocation()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, CommercialEventsTestJson.EventDetail);

        await CreateClient(handler).MoveCommercialEvent("EV-7K3M", new CommercialEventMoveRequest { AllocationId = "alloc-2" }, "Admin#1");

        AssertRequest(handler, 0, HttpMethod.Post, "/admin/commercial-events/events/EV-7K3M/move");
        Assert.That(handler.RequestBodies[0], Is.EqualTo("""{"allocationId":"alloc-2","actingBattleTag":"Admin#1"}"""));
    }

    [Test]
    public async Task CloseAndUnsuspendPostOnlyTheActingBattleTag()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, CommercialEventsTestJson.EventDetail);
        var client = CreateClient(handler);

        await client.CloseCommercialEvent("EV-7K3M", "Admin#1");
        await client.UnsuspendCommercialEvent("EV-7K3M", "Admin#1");

        AssertRequest(handler, 0, HttpMethod.Post, "/admin/commercial-events/events/EV-7K3M/close");
        AssertRequest(handler, 1, HttpMethod.Post, "/admin/commercial-events/events/EV-7K3M/unsuspend");
        Assert.That(handler.RequestBodies, Is.EqualTo(new[] { ActingOnlyBody, ActingOnlyBody }));
    }

    [Test]
    public async Task SuspendEventPostsTheMessageAndNote()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, CommercialEventsTestJson.EventDetail);

        await CreateClient(handler).SuspendCommercialEvent("EV-7K3M", new CommercialEventSuspendRequest
        {
            SuspensionMessage = "Paused while we review the results",
            AdminNote = "Reported on Discord",
        }, "Admin#1");

        AssertRequest(handler, 0, HttpMethod.Post, "/admin/commercial-events/events/EV-7K3M/suspend");
        Assert.That(handler.RequestBodies[0], Is.EqualTo(
            """{"suspensionMessage":"Paused while we review the results","adminNote":"Reported on Discord","actingBattleTag":"Admin#1"}"""));
    }

    [Test]
    public async Task GetPeopleParsesOrganizersDelegatesAndHosts()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, CommercialEventsTestJson.People);

        var people = await CreateClient(handler).GetCommercialEventPeople("EV-7K3M");

        AssertRequest(handler, 0, HttpMethod.Get, "/admin/commercial-events/events/EV-7K3M/people");
        Assert.That(people.Organizers, Is.EqualTo(new[] { "Organizer#1234" }));
        Assert.That(people.Delegates.Single().BattleTag, Is.EqualTo("Delegate#2345"));
        Assert.That(people.Delegates.Single().AddedAt, Is.EqualTo(Utc(2026, 10, 8, 11)));
        Assert.That(people.Hosts.Single().BattleTag, Is.EqualTo("Host#3456"));
    }

    [Test]
    public async Task PersonRoutesEscapeTheBattleTag()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, CommercialEventsTestJson.People);
        var client = CreateClient(handler);

        await client.AddCommercialEventPerson("EV-7K3M", "Grubby#1234", new CommercialEventPersonRequest { Role = "host" }, "Admin#1");
        await client.RemoveCommercialEventPerson("EV-7K3M", "Grubby#1234", "Admin#1");

        AssertRequest(handler, 0, HttpMethod.Put, "/admin/commercial-events/events/EV-7K3M/people/Grubby%231234");
        AssertRequest(handler, 1, HttpMethod.Delete, "/admin/commercial-events/events/EV-7K3M/people/Grubby%231234");
        Assert.That(handler.RequestBodies[0], Is.EqualTo("""{"role":"host","actingBattleTag":"Admin#1"}"""));
        Assert.That(handler.RequestBodies[1], Is.EqualTo(ActingOnlyBody));
    }

    [Test]
    public async Task GetGamesForwardsCursorAndLimitAndParsesThePage()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, CommercialEventsTestJson.GamesPage);

        var page = await CreateClient(handler).GetCommercialEventGames("EV-7K3M", "abc/+=", "25");

        AssertRequest(handler, 0, HttpMethod.Get, "/admin/commercial-events/events/EV-7K3M/games", "?cursor=abc%2F%2B%3D&limit=25");
        Assert.That(page.NextCursor, Is.EqualTo("c2"));
        var game = page.Games.Single();
        Assert.That(game.MatchId, Is.EqualTo("m-1"));
        Assert.That(game.LobbyName, Is.EqualTo("Showmatch 1"));
        Assert.That(game.StartedAt, Is.EqualTo(Utc(2026, 10, 9, 18, 5)));
        Assert.That(game.Outcome, Is.EqualTo("valid"));
        Assert.That(game.InvalidReason, Is.Null);
        Assert.That(game.LengthSeconds, Is.EqualTo(312));
        Assert.That(game.NamesHidden, Is.False);
        Assert.That(game.Host, Is.EqualTo("Host#3456"));
        Assert.That(game.Teams, Has.Count.EqualTo(2));
        Assert.That(game.Teams[0].PlayerCount, Is.EqualTo(1));
        Assert.That(game.Teams[0].Players.Single().BattleTag, Is.EqualTo("Grubby#1234"));
        Assert.That(game.Teams[0].Players.Single().Won, Is.False);
        Assert.That(game.Teams[1].Players.Single().Won, Is.True);
        Assert.That(game.ObserverCount, Is.EqualTo(1));
        Assert.That(game.Observers, Is.EqualTo(new[] { "Caster#1111" }));
        Assert.That(game.Computers, Is.EqualTo(0));
        Assert.That(game.ViewerCount, Is.EqualTo(42));
        Assert.That(game.WatchedSecondsTotal, Is.EqualTo(12600));
        Assert.That(game.WatchedSecondsAvg, Is.EqualTo(300));
    }

    [Test]
    public async Task GetActiveGamesParsesDtos()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, CommercialEventsTestJson.ActiveGames);

        var games = await CreateClient(handler).GetActiveCommercialEventGames();

        AssertRequest(handler, 0, HttpMethod.Get, "/admin/commercial-events/games/active");
        var game = games.Single();
        Assert.That(game.MatchId, Is.EqualTo("m-2"));
        Assert.That(game.EventId, Is.EqualTo("EV-7K3M"));
        Assert.That(game.EventName, Is.EqualTo("Friday Showmatch"));
        Assert.That(game.Host, Is.EqualTo("Host#3456"));
        Assert.That(game.LobbyName, Is.EqualTo("Showmatch 2"));
        Assert.That(game.StartedAt, Is.EqualTo(Utc(2026, 10, 9, 19)));
        Assert.That(game.Players, Is.EqualTo(new[] { "Grubby#1234", "Moon#5678" }));
        Assert.That(game.Observers, Is.Empty);
        Assert.That(game.ViewerCount, Is.EqualTo(17));
    }

    [Test]
    public async Task TerminatePostsTheActingBattleTagAndAcceptsNoContent()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.NoContent);

        await CreateClient(handler).TerminateCommercialEventGame("m-2", "Admin#1");

        AssertRequest(handler, 0, HttpMethod.Post, "/admin/commercial-events/games/m-2/terminate");
        Assert.That(handler.RequestBodies[0], Is.EqualTo(ActingOnlyBody));
    }

    [Test]
    public async Task GetAuditForwardsTheGivenScopeAndParsesEntries()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, CommercialEventsTestJson.Audit);

        var entries = await CreateClient(handler).GetCommercialEventAudit(null, "alloc-1");

        AssertRequest(handler, 0, HttpMethod.Get, "/admin/commercial-events/audit", "?allocationId=alloc-1");
        var entry = entries.Single();
        Assert.That(entry.Id, Is.EqualTo("aud-1"));
        Assert.That(entry.At, Is.EqualTo(Utc(2026, 10, 9, 19)));
        Assert.That(entry.Actor, Is.EqualTo("Admin#1"));
        Assert.That(entry.ActorRole, Is.EqualTo("admin"));
        Assert.That(entry.Action, Is.EqualTo("event-updated"));
        Assert.That(entry.EventId, Is.EqualTo("EV-7K3M"));
        Assert.That(entry.AllocationId, Is.Null);
        Assert.That(entry.Details, Is.Not.Null);
    }

    [Test]
    public async Task AuditDetailsSurviveTheSystemTextJsonResponseToTheWebsite()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, CommercialEventsTestJson.Audit);

        var entries = await CreateClient(handler).GetCommercialEventAudit("EV-7K3M", null);

        // ASP.NET writes website responses with System.Text.Json, which cannot serialize a Newtonsoft JObject.
        var websiteJson = JsonSerializer.Serialize(entries, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var expected = JToken.Parse(CommercialEventsTestJson.Audit)[0]!["details"];
        var actual = JToken.Parse(websiteJson)[0]!["details"];
        Assert.That(JToken.DeepEquals(actual, expected), Is.True, websiteJson);
        // Date strings inside details are passed through unchanged, milliseconds included.
        Assert.That(websiteJson, Does.Contain("\"from\":\"2026-10-09T23:00:00.000Z\""));
        Assert.That(handler.Requests[0].RequestUri!.Query, Is.EqualTo("?eventId=EV-7K3M"));
    }

    [Test]
    public async Task GetRoleHintsPostsTheBattleTagsWithoutActingBattleTag()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, CommercialEventsTestJson.RoleHints);

        var hints = await CreateClient(handler).GetCommercialEventRoleHints(["Grubby#1234", "Moon#5678"]);

        // Index P41: a read with a JSON body; no query string and no actingBattleTag.
        AssertRequest(handler, 0, HttpMethod.Post, "/admin/commercial-events/roles/lookup");
        Assert.That(handler.RequestBodies[0], Is.EqualTo("""{"battleTags":["Grubby#1234","Moon#5678"]}"""));
        Assert.That(hints.Select(h => h.BattleTag), Is.EqualTo(new[] { "Grubby#1234", "Moon#5678" }));
        Assert.That(hints[0].OrganizerOf.Single().AllocationId, Is.EqualTo("alloc-1"));
        Assert.That(hints[0].OrganizerOf.Single().AllocationName, Is.EqualTo("Weekly showmatches"));
        Assert.That(hints[0].DelegateOf, Is.Empty);
        Assert.That(hints[0].HostOf.Single().EventId, Is.EqualTo("EV-7K3M"));
        Assert.That(hints[0].HostOf.Single().EventName, Is.EqualTo("Friday Showmatch"));
        Assert.That(hints[1].HostOf, Is.Empty);
    }

    private static void AssertRequest(StubMatchmakingHandler handler, int index, HttpMethod method, string path, string query = "")
    {
        var request = handler.Requests[index];
        Assert.That(request.Method, Is.EqualTo(method));
        Assert.That(request.Headers.GetValues("x-admin-secret"), Is.EqualTo(new[] { ExpectedAdminSecret }));
        Assert.That(request.RequestUri!.AbsolutePath, Is.EqualTo(path));
        Assert.That(request.RequestUri.Query, Is.EqualTo(query));
    }
}
