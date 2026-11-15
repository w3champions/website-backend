using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Moq;
using NUnit.Framework;
using W3C.Contracts.Admin.Permission;
using W3ChampionsStatisticService.Admin;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using W3ChampionsStatisticService.WebApi.ExceptionFilters;

namespace WC3ChampionsStatisticService.Tests.Admin;

/// <summary>
/// BansController over real HTTP: Kestrel on a loopback port, routing, MVC binding, the real permission and acting-player
/// filters and the global exception filters as Program.cs adds them. Only the token check and the repository are doubles.
/// </summary>
[TestFixture]
public class BansEndpointTests
{
    private const string ModeratorToken = "moderator.jwt";
    private const string AdminToken = "admin-moderator.jwt";
    private const string InvalidToken = "invalid.jwt";
    private const string Moderator = "ModOne#3001";

    private WebApplication _app;
    private HttpClient _http;
    private Mock<IAdminRepository> _repository;

    [OneTimeSetUp]
    public async Task StartServer()
    {
        _repository = new Mock<IAdminRepository>();
        var auth = new Mock<IW3CAuthenticationService>();
        auth.Setup(a => a.GetUserByToken(It.IsAny<string>(), It.IsAny<bool>()))
            .Throws(new SecurityTokenValidationException("Invalid token"));
        auth.Setup(a => a.GetUserByToken(ModeratorToken, It.IsAny<bool>())).Returns(User(EPermission.Moderation));
        auth.Setup(a => a.GetUserByToken(AdminToken, It.IsAny<bool>())).Returns(User(EPermission.Moderation, EPermission.AdminModeration));

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddControllers(c =>
            {
                c.Filters.Add<ValidationExceptionFilter>();
                c.Filters.Add<HttpRequestExceptionFilter>();
            })
            .AddApplicationPart(typeof(BansController).Assembly)
            .ConfigureApplicationPartManager(parts =>
            {
                parts.FeatureProviders.Clear();
                parts.FeatureProviders.Add(new OnlyTheBansController());
            });
        builder.Services.AddSingleton(auth.Object);
        builder.Services.AddTransient<InjectActingPlayerFromAuthCodeFilter>();
        builder.Services.AddSingleton(_repository.Object);

        _app = builder.Build();
        _app.UseRouting();
        _app.MapControllers();
        await _app.StartAsync();
        _http = new HttpClient { BaseAddress = new Uri(_app.Urls.Single()) };
    }

    [OneTimeTearDown]
    public async Task StopServer()
    {
        _http?.Dispose();
        if (_app != null) await _app.DisposeAsync();
    }

    [SetUp]
    public void SetUp()
    {
        _repository.Reset();
        _repository.SetReturnsDefault(Task.FromResult(new MatchmakingProxyResult(200, "{}")));
    }

    private static W3CUserAuthenticationDto User(params EPermission[] permissions) =>
        new() { BattleTag = Moderator, IsAdmin = true, Permissions = permissions.ToHashSet() };

    private async Task<(int Status, string Body)> Send(HttpMethod method, string path, string token, string json = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (json != null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request);
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static string ErrorCode(string body) => JsonNode.Parse(body)!["errors"]![0]!["code"]!.GetValue<string>();

    [Test]
    public async Task Create_SendsTheTokenTagAsActor_NotTheBodyActor()
    {
        var (status, _) = await Send(HttpMethod.Post, "/api/admin/bans", ModeratorToken,
            """{"accountKey":"1001","actor":"Тестер#1002","machineAccess":true,"reason":"test"}""");

        Assert.That(status, Is.EqualTo(200));
        _repository.Verify(r => r.CreateBan(It.Is<JsonObject>(b => (string)b["accountKey"] == "1001"), Moderator, false));
        _repository.VerifyNoOtherCalls();
    }

    [Test]
    public async Task Create_EmptyBody_Answers400Validation_WithoutCallingTheRepository()
    {
        var (status, body) = await Send(HttpMethod.Post, "/api/admin/bans", ModeratorToken, "");

        Assert.That((status, ErrorCode(body)), Is.EqualTo((400, "VALIDATION")));
        _repository.VerifyNoOtherCalls();
    }

    [TestCase("[]")]
    [TestCase("\"text\"")]
    [TestCase("null")]
    public async Task Create_BodyThatIsNotAnObject_Answers400Validation_WithoutCallingTheRepository(string json)
    {
        var (status, body) = await Send(HttpMethod.Post, "/api/admin/bans", ModeratorToken, json);

        Assert.That((status, ErrorCode(body)), Is.EqualTo((400, "VALIDATION")));
        _repository.VerifyNoOtherCalls();
    }

    [Test]
    public async Task Create_ActorInAnyCase_IsReplacedByTheTokenTag()
    {
        JsonObject seen = null;
        _repository.Setup(r => r.CreateBan(It.IsAny<JsonObject>(), It.IsAny<string>(), It.IsAny<bool>()))
            .Callback<JsonObject, string, bool>((b, _, _) => seen = b)
            .ReturnsAsync(new MatchmakingProxyResult(200, "{}"));

        await Send(HttpMethod.Post, "/api/admin/bans", ModeratorToken, """{"accountKey":"1001","Actor":"Тестер#1002"}""");

        var forwarded = JsonNode.Parse(AdminRepository.ForwardedBody(seen, Moderator))!.AsObject();
        Assert.That(forwarded.Select(p => p.Key), Is.EquivalentTo(new[] { "accountKey", "actor" }));
        Assert.That((string)forwarded["actor"], Is.EqualTo(Moderator));
    }

    [TestCase(null)]
    [TestCase(InvalidToken)]
    public async Task MissingOrInvalidToken_Answers401_WithoutCallingTheRepository(string token)
    {
        var (getStatus, _) = await Send(HttpMethod.Get, "/api/admin/bans", token);
        var (postStatus, _) = await Send(HttpMethod.Post, "/api/admin/bans", token, """{"accountKey":"1001"}""");

        Assert.That((getStatus, postStatus), Is.EqualTo((401, 401)));
        _repository.VerifyNoOtherCalls();
    }

    [Test]
    public async Task AccountKey_WithAnEncodedSlash_ReachesTheActionAsOneValue()
    {
        var (status, _) = await Send(HttpMethod.Get, "/api/admin/bans/accounts/ab%2F12", ModeratorToken);

        Assert.That(status, Is.EqualTo(200));
        _repository.Verify(r => r.GetBanAccount("ab/12", false));
        _repository.VerifyNoOtherCalls();
    }

    [TestCase("GET", "/api/admin/bans/accounts/1001/machine-ids", null)]
    [TestCase("GET", "/api/admin/bans/machine-ids/aa11bb22/accounts?machineAccess=true", null)]
    [TestCase("POST", "/api/admin/bans/machine-impact?machineAccess=true", """{"accountKey":"1001","machineIds":["aa11bb22"],"machineAccess":true}""")]
    public async Task MachineRoutes_AsModeratorWithoutAdminModeration_Answer403_WithoutCallingTheRepository(string method, string path, string json)
    {
        var (status, body) = await Send(new HttpMethod(method), path, ModeratorToken, json);

        Assert.That((status, ErrorCode(body)), Is.EqualTo((403, "MACHINE_BAN_PERMISSION")));
        _repository.VerifyNoOtherCalls();
    }

    [Test]
    public async Task MachineRoutes_AsAdminModerator_ReachTheRepository()
    {
        await Send(HttpMethod.Get, "/api/admin/bans/machine-ids/cc33dd44/accounts?excludeAccount=1001", AdminToken);

        _repository.Verify(r => r.GetBanMachineIdAccounts("cc33dd44", "1001", true));
        _repository.VerifyNoOtherCalls();
    }

    [Test]
    public async Task MachineAccess_FromTheClient_HasNoEffect()
    {
        await Send(HttpMethod.Get, "/api/admin/bans?machineAccess=true&status=inEffect", ModeratorToken);
        await Send(HttpMethod.Post, "/api/admin/bans/status?machineAccess=true", ModeratorToken, """{"accounts":["1001"],"machineAccess":true}""");
        await Send(HttpMethod.Get, "/api/admin/bans/authors", AdminToken);

        _repository.Verify(r => r.ListBans(It.Is<BanListFilter>(f => f.Status == "inEffect"), false));
        _repository.Verify(r => r.GetBanStatuses(It.IsAny<JsonObject>(), false));
        _repository.Verify(r => r.GetBanAuthors(true));
        _repository.VerifyNoOtherCalls();
    }

    [TestCase("""{"accountKey":"1001","actor":"Tester#1001","actor":"Тестер#1002"}""")]
    [TestCase("""{"accountKey":"1001","Actor":"Tester#1001","ACTOR":"Тестер#1002"}""")]
    [TestCase("""{"accountKey":"1001","scope":{"note":"a","note":"b"}}""")]
    [TestCase("""{"accountKey":"1001","list":[{"Note":"a","note":"b"}]}""")]
    public async Task DuplicateKeys_Answer400Validation_WithoutCallingTheRepository(string json)
    {
        var (createStatus, createBody) = await Send(HttpMethod.Post, "/api/admin/bans", ModeratorToken, json);
        var (editStatus, editBody) = await Send(HttpMethod.Patch, "/api/admin/bans/Ban1001", ModeratorToken, json);
        var (impactStatus, impactBody) = await Send(HttpMethod.Post, "/api/admin/bans/machine-impact", AdminToken, json);

        Assert.That((createStatus, ErrorCode(createBody)), Is.EqualTo((400, "VALIDATION")));
        Assert.That((editStatus, ErrorCode(editBody)), Is.EqualTo((400, "VALIDATION")));
        Assert.That((impactStatus, ErrorCode(impactBody)), Is.EqualTo((400, "VALIDATION")));
        _repository.VerifyNoOtherCalls();
    }

    private sealed class OnlyTheBansController : ControllerFeatureProvider
    {
        protected override bool IsController(TypeInfo typeInfo) => typeInfo.AsType() == typeof(BansController);
    }
}
