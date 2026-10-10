using System;
using System.Linq;
using System.Net;
using System.Net.Http;
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
