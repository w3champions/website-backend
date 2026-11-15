using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Web;
using MongoDB.Driver;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using W3C.Domain;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Admin;
using W3ChampionsStatisticService.Ports;

namespace WC3ChampionsStatisticService.Tests.Admin;

[TestFixture]
public class BansProxyRepositoryTests
{
    // MongoClient connects lazily; these tests never touch the database.
    private static readonly MongoClient UnusedMongo = new("mongodb://localhost:1");

    private static (AdminRepository Repository, StubMatchmakingHandler Handler, RecordingHttpClientFactory Factory) Create(HttpStatusCode status = HttpStatusCode.OK, string body = "{}")
    {
        var handler = new StubMatchmakingHandler(status, body);
        var factory = new RecordingHttpClientFactory(handler);
        return (new AdminRepository(UnusedMongo, factory), handler, factory);
    }

    private const string Account = "1001";
    private const string BanId = "Ban1001";
    private const string MachineId = "m1001";
    private static JsonObject AnyBody() => JsonNode.Parse("{\"version\":3}")!.AsObject();

    // One call per route, numbered as in the cases below. machineAccess is ignored by routes 7-9, which always send it.
    private static Task<MatchmakingProxyResult> Invoke(AdminRepository r, int route, bool machineAccess) => route switch
    {
        0 => r.ListBans(new BanListFilter(null, null, null, null, null, null, null, null), machineAccess),
        1 => r.GetBanAuthors(machineAccess),
        2 => r.LookupBanAccounts("Tes", machineAccess),
        3 => r.GetBanAccount(Account, machineAccess),
        4 => r.CreateBan(AnyBody(), "Mod One", machineAccess),
        5 => r.EditBan(BanId, AnyBody(), "Mod One", machineAccess),
        6 => r.RevokeBan(BanId, AnyBody(), "Mod One", machineAccess),
        7 => r.GetBanAccountMachineIds(Account),
        8 => r.GetBanMachineIdAccounts(MachineId, null),
        9 => r.GetBanMachineImpact(AnyBody()),
        10 => r.GetBanStatuses(AnyBody(), machineAccess),
        _ => throw new ArgumentOutOfRangeException(nameof(route)),
    };

    [TestCase(0, "GET", "/admin/bans")]
    [TestCase(1, "GET", "/admin/bans/authors")]
    [TestCase(2, "GET", "/admin/bans/account-lookup")]
    [TestCase(3, "GET", "/admin/bans/accounts/1001")]
    [TestCase(4, "POST", "/admin/bans")]
    [TestCase(5, "PATCH", "/admin/bans/Ban1001")]
    [TestCase(6, "POST", "/admin/bans/Ban1001/revoke")]
    [TestCase(7, "GET", "/admin/bans/accounts/1001/machine-ids")]
    [TestCase(8, "GET", "/admin/bans/machine-ids/m1001/accounts")]
    [TestCase(9, "POST", "/admin/bans/machine-impact")]
    [TestCase(10, "POST", "/admin/bans/status")]
    public async Task EveryRoute_UsesMethodPath_SecretHeader_AndNamedClient(int route, string method, string path)
    {
        var (repository, handler, factory) = Create();
        await Invoke(repository, route, machineAccess: false);
        var request = handler.Requests.Single();
        Assert.That(request.Method.Method, Is.EqualTo(method));
        Assert.That(request.RequestUri!.AbsolutePath, Does.EndWith(path));
        Assert.That(request.Headers.GetValues(AdminSecretProvider.HeaderName).Single(), Is.EqualTo(AdminSecretTestEnvironment.Secret));
        Assert.That(factory.RequestedNames, Is.All.EqualTo(HttpClientNames.Matchmaking));
    }

    [Test]
    public async Task MachineAccess_IsSentOnlyWhenGranted([Values(0, 1, 2, 3, 4, 5, 6, 10)] int route, [Values] bool granted)
    {
        var (repository, handler, _) = Create();
        await Invoke(repository, route, granted);
        var query = HttpUtility.ParseQueryString(handler.Requests.Single().RequestUri!.Query);
        Assert.That(query["machineAccess"], granted ? Is.EqualTo("true") : Is.Null);
    }

    [Test]
    public async Task MachineRoutes_AlwaysSendMachineAccess([Values(7, 8, 9)] int route)
    {
        var (repository, handler, _) = Create();
        await Invoke(repository, route, machineAccess: false);
        var query = HttpUtility.ParseQueryString(handler.Requests.Single().RequestUri!.Query);
        Assert.That(query["machineAccess"], Is.EqualTo("true"));
    }

    [Test]
    public async Task List_ForwardsEachFilterAsOneEncodedValue_AndOmitsAbsentOnes()
    {
        var (repository, handler, _) = Create();
        await repository.ListBans(new BanListFilter("inEffect", "Helper(bot)", "tag:Tester#1001&machineAccess=true", null, "ends", "asc", "2", "50"), machineAccess: false);
        var uri = handler.Requests.Single().RequestUri!;
        var query = HttpUtility.ParseQueryString(uri.Query);
        Assert.That(query.AllKeys, Is.EquivalentTo(new[] { "status", "author", "account", "sort", "dir", "page", "pageSize" }));
        Assert.That(query["author"], Is.EqualTo("Helper(bot)"));
        Assert.That(query["account"], Is.EqualTo("tag:Tester#1001&machineAccess=true"));
        Assert.That(query["machineAccess"], Is.Null);
        Assert.That(uri.Fragment, Is.Empty);
    }

    [TestCase("1001")]
    [TestCase("tag:Tester#1001")]
    [TestCase("tag:Тестер#1002")]
    [TestCase("tag:тестер#1002")]
    [TestCase("ab/12")]
    public async Task AccountKeys_AreSentAsOneEncodedSegment(string accountKey)
    {
        var (repository, handler, _) = Create();
        await repository.GetBanAccount(accountKey, machineAccess: false);
        var uri = handler.Requests.Single().RequestUri!;
        Assert.That(uri.AbsoluteUri, Does.Not.Contain("#"));
        Assert.That(Uri.UnescapeDataString(uri.AbsolutePath.Split('/').Last()), Is.EqualTo(accountKey));
    }

    // Exact outgoing paths: each value is encoded exactly once.
    [TestCase(3, "tag:Tester#1001", "/admin/bans/accounts/tag%3ATester%231001")]
    [TestCase(3, "ab/12", "/admin/bans/accounts/ab%2F12")]
    [TestCase(5, "ab/12", "/admin/bans/ab%2F12")]
    [TestCase(8, "ab/12", "/admin/bans/machine-ids/ab%2F12/accounts")]
    public async Task PathValues_AreEncodedExactlyOnce(int route, string value, string path)
    {
        var (repository, handler, _) = Create();
        await (route switch
        {
            3 => repository.GetBanAccount(value, false),
            5 => repository.EditBan(value, AnyBody(), "Mod One", false),
            _ => repository.GetBanMachineIdAccounts(value, null),
        });
        Assert.That(handler.Requests.Single().RequestUri!.AbsolutePath, Does.EndWith(path));
    }

    [Test]
    public async Task Lookup_And_ExcludeAccount_AreSentAsOneEncodedValue()
    {
        var (repository, handler, _) = Create();
        await repository.LookupBanAccounts("Тес#&q=x", machineAccess: false);
        await repository.GetBanMachineIdAccounts(MachineId, "tag:Tester#1001");
        var lookup = HttpUtility.ParseQueryString(handler.Requests[0].RequestUri!.Query);
        Assert.That(lookup.AllKeys, Is.EquivalentTo(new[] { "q" }));
        Assert.That(lookup["q"], Is.EqualTo("Тес#&q=x"));
        var accounts = HttpUtility.ParseQueryString(handler.Requests[1].RequestUri!.Query);
        Assert.That(accounts["excludeAccount"], Is.EqualTo("tag:Tester#1001"));
        Assert.That(accounts["machineAccess"], Is.EqualTo("true"));
    }

    [Test]
    public void ForwardedBody_SetsTheGivenActor_OverAnyActorTheClientSent()
    {
        var body = JsonNode.Parse("{\"actor\":\"Mod Two\",\"banReason\":\"grief\",\"battleTag\":\"Тестер#1002\"}")!.AsObject();
        var sent = JsonNode.Parse(AdminRepository.ForwardedBody(body, "Mod One"))!.AsObject();
        Assert.That(sent["actor"]!.GetValue<string>(), Is.EqualTo("Mod One"));
        Assert.That(sent["battleTag"]!.GetValue<string>(), Is.EqualTo("Тестер#1002"));
        Assert.That(sent["banReason"]!.GetValue<string>(), Is.EqualTo("grief"));
    }

    [Test]
    public void ForwardedBody_DropsAClientMachineAccess_AndLeavesTheInputUnchanged()
    {
        var body = JsonNode.Parse("{\"machineAccess\":true,\"accountKey\":\"1001\"}")!.AsObject();
        var sent = JsonNode.Parse(AdminRepository.ForwardedBody(body, null))!.AsObject();
        Assert.That(sent.ContainsKey("machineAccess"), Is.False);
        Assert.That(sent.ContainsKey("actor"), Is.False);
        Assert.That(sent["accountKey"]!.GetValue<string>(), Is.EqualTo("1001"));
        Assert.That(body.ContainsKey("machineAccess"), Is.True);
    }

    [Test]
    public async Task WriteRoutes_SendTheActorInTheBody([Values(4, 5, 6)] int route)
    {
        var (repository, handler, _) = Create();
        var body = JsonNode.Parse("{\"actor\":\"Mod Two\",\"version\":3}")!.AsObject();
        await (route switch
        {
            4 => repository.CreateBan(body, "Mod One", false),
            5 => repository.EditBan(BanId, body, "Mod One", false),
            _ => repository.RevokeBan(BanId, body, "Mod One", false),
        });
        var sent = JObject.Parse(handler.RequestBodies.Single());
        Assert.That(sent["actor"]!.Value<string>(), Is.EqualTo("Mod One"));
        Assert.That(sent["version"]!.Value<int>(), Is.EqualTo(3));
        Assert.That(handler.Requests.Single().Content!.Headers.ContentType!.MediaType, Is.EqualTo("application/json"));
    }

    [TestCase(HttpStatusCode.Created, "{\"id\":\"Ban1001\"}")]
    [TestCase(HttpStatusCode.BadRequest, "{\"errors\":[{\"msg\":\"bad\",\"code\":\"VALIDATION\"}]}")]
    [TestCase(HttpStatusCode.NotFound, "{\"errors\":[{\"msg\":\"no\",\"code\":\"BAN_NOT_FOUND\"}]}")]
    [TestCase(HttpStatusCode.Conflict, "{\"errors\":[{\"msg\":\"changed\",\"code\":\"BAN_VERSION_CONFLICT\"}]}")]
    [TestCase(HttpStatusCode.Conflict, "{\"errors\":[{\"msg\":\"ended\",\"code\":\"BAN_NOT_IN_EFFECT\"}]}")]
    [TestCase(HttpStatusCode.ServiceUnavailable, "{\"errors\":[{\"msg\":\"later\",\"code\":\"BANS_UNAVAILABLE\"}]}")]
    [TestCase(HttpStatusCode.Forbidden, "{\"errors\":[{\"msg\":\"Permission denied, please contact an Administrator\",\"code\":\"MACHINE_BAN_PERMISSION\"}]}")]
    public async Task MatchmakingStatusAndBody_PassThroughUnchanged(HttpStatusCode status, string body)
    {
        var (repository, _, _) = Create(status, body);
        var result = await repository.EditBan(BanId, AnyBody(), "Mod One", false);
        Assert.That(result.StatusCode, Is.EqualTo((int)status));
        Assert.That(result.Body, Is.EqualTo(body));
    }

    [Test]
    public async Task Matchmaking403WithoutTheMachineCode_BecomesGeneric502()
    {
        var (repository, _, _) = Create(HttpStatusCode.Forbidden, "{\"errors\":[{\"msg\":\"Permission denied\"}]}");
        var result = await repository.EditBan(BanId, AnyBody(), "Mod One", false);
        Assert.That(result.StatusCode, Is.EqualTo(502));
        Assert.That(JObject.Parse(result.Body)["error"]!.Value<string>(), Is.EqualTo(MatchmakingProxyResult.UnavailableMessage));
    }

    [TestCase("{\"errors\":[{\"msg\":\"x\",\"code\":\"MACHINE_BAN_PERMISSION\"}]}", true)]
    [TestCase("{\"errors\":[{\"msg\":\"Permission denied\"}]}", false)]
    [TestCase("{\"errors\":\"MACHINE_BAN_PERMISSION\"}", false)]
    [TestCase("not json", false)]
    [TestCase("", false)]
    public void IsMachineBanPermissionRefusal_OnlyForTheCode(string body, bool expected) =>
        Assert.That(AdminRepository.IsMachineBanPermissionRefusal(body), Is.EqualTo(expected));

    [TestCase(typeof(HttpRequestException))]
    [TestCase(typeof(TaskCanceledException))]
    public async Task NetworkFailure_BecomesGeneric502(Type exceptionType)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType, "connect to https://mm.internal failed")!;
        var repository = new AdminRepository(UnusedMongo, new RecordingHttpClientFactory(new ThrowingMatchmakingHandler(exception)));
        var result = await repository.RevokeBan(BanId, AnyBody(), "Mod One", false);
        Assert.That(result.StatusCode, Is.EqualTo(502));
        Assert.That(result.Body, Does.Not.Contain("mm.internal"));
    }

    [Test]
    public void BodiesAndMachineIds_AreNotTraced()
    {
        var parameters = typeof(IAdminRepository).GetMethods()
            .Where(m => m.Name.Contains("Ban"))
            .SelectMany(m => m.GetParameters())
            .Where(p => p.ParameterType == typeof(JsonObject) || p.Name == "machineId")
            .ToList();
        Assert.That(parameters, Has.Count.EqualTo(6)); // bodies of CreateBan, EditBan, RevokeBan, GetBanMachineImpact, GetBanStatuses; machineId
        Assert.That(parameters.All(p => p.GetCustomAttribute<NoTraceAttribute>() != null), Is.True);
    }
}
