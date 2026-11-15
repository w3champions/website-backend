using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Routing;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using W3C.Contracts.Admin.Permission;
using W3ChampionsStatisticService.Admin;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace WC3ChampionsStatisticService.Tests.Admin;

[TestFixture]
public class BansControllerTests
{
    private Mock<IAdminRepository> _repository;
    private BansController _controller;

    [SetUp]
    public void SetUp()
    {
        _repository = new Mock<IAdminRepository>();
        _repository.SetReturnsDefault(Task.FromResult(new MatchmakingProxyResult(200, "{}")));
        _controller = new BansController(_repository.Object);
    }

    private void AsUser(params EPermission[] permissions)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?machineAccess=true&actor=Mod%20Two");   // client tries to set both
        context.Items[InjectActingPlayerFromAuthCodeFilter.ActingPlayerUserKey] =
            new W3CUserAuthenticationDto { BattleTag = "Mod One", Permissions = permissions.ToHashSet() };
        _controller.ControllerContext = new ControllerContext { HttpContext = context };
    }

    private static JsonElement Body(string json) => JsonDocument.Parse(json).RootElement;

    private static string ErrorCode(IActionResult result) =>
        JObject.FromObject(((ObjectResult)result).Value!)["errors"]![0]!["code"]!.Value<string>();

    private static readonly string[] Actions = { "List", "Authors", "Lookup", "Account", "Create", "Edit", "Revoke",
        "MachineIds", "MachineIdAccounts", "MachineImpact", "Status" };

    [TestCaseSource(nameof(Actions))]
    public void EveryAction_RequiresModeration_AndInjectsTheCaller(string action)
    {
        var method = typeof(BansController).GetMethod(action)!;
        Assert.That(method.GetCustomAttribute<BearerHasPermissionFilter>()!.Permission, Is.EqualTo(EPermission.Moderation));
        Assert.That(method.GetCustomAttribute<InjectActingPlayerAuthCodeAttribute>(), Is.Not.Null);
        var names = method.GetParameters().Select(p => p.Name).ToList();
        Assert.That(names, Has.No.Member("machineAccess"));
        Assert.That(names, Has.No.Member("actor"));
        Assert.That(names, Has.No.Member("battleTag"));
    }

    private static readonly string[] BodyActions = { "Create", "Edit", "Revoke", "MachineImpact", "Status" };

    // Without Allow, [ApiController] answers an empty body with its own 400 before the permission filter and the action run.
    [TestCaseSource(nameof(BodyActions))]
    public void BodyActions_AllowAnEmptyBody_AndBoundTheRequestSize(string action)
    {
        var method = typeof(BansController).GetMethod(action)!;
        var body = method.GetParameters().Single(p => p.Name == "body");
        Assert.That(body.GetCustomAttribute<FromBodyAttribute>()!.EmptyBodyBehavior, Is.EqualTo(EmptyBodyBehavior.Allow));
        Assert.That(((IRequestSizeLimitMetadata)method.GetCustomAttribute<RequestSizeLimitAttribute>()!).MaxRequestBodySize, Is.EqualTo(BansController.MaxJsonBodyBytes));
    }

    [Test]
    public void Controller_ExposesExactlyTheElevenContractRoutes()
    {
        var actions = typeof(BansController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        var routes = actions.Select(m => m.GetCustomAttribute<HttpMethodAttribute>()!)
            .Select(a => $"{a.HttpMethods.Single()} {a.Template}");
        Assert.That(typeof(BansController).GetCustomAttribute<RouteAttribute>()!.Template, Is.EqualTo("api/admin/bans"));
        Assert.That(routes, Is.EquivalentTo(new[] { "GET ", "GET authors", "GET account-lookup", "GET accounts/{accountKey}",
            "POST ", "PATCH {id}", "POST {id}/revoke", "GET accounts/{accountKey}/machine-ids",
            "GET machine-ids/{machineId}/accounts", "POST machine-impact", "POST status" }));
    }

    [Test]
    public async Task ReadRoutes_ForwardMachineAccess_OnlyForAdminModeration([Values] bool admin)
    {
        AsUser(admin ? new[] { EPermission.Moderation, EPermission.AdminModeration } : new[] { EPermission.Moderation });
        await _controller.List("inEffect", null, "1001", "true", null, null, null, null);
        await _controller.Authors();
        await _controller.Lookup("Tes");
        await _controller.Account("tag:Tester#1001");
        await _controller.Status(Body("{\"battleTags\":[\"Tester#1001\"]}"));
        _repository.Verify(r => r.ListBans(new BanListFilter("inEffect", null, "1001", "true", null, null, null, null), admin));
        _repository.Verify(r => r.GetBanAuthors(admin));
        _repository.Verify(r => r.LookupBanAccounts("Tes", admin));
        _repository.Verify(r => r.GetBanAccount("tag:Tester#1001", admin));
        _repository.Verify(r => r.GetBanStatuses(It.IsAny<JsonObject>(), admin));
    }

    [Test]
    public async Task WriteRoutes_SendTheTokenTagAsActor_EvenWhenTheClientSentAnother([Values] bool admin)
    {
        AsUser(admin ? new[] { EPermission.Moderation, EPermission.AdminModeration } : new[] { EPermission.Moderation });
        await _controller.Create(Body("{\"actor\":\"Mod Two\",\"machineAccess\":true}"));
        await _controller.Edit("Ban1001", Body("{\"actor\":\"Mod Two\",\"version\":3}"));
        await _controller.Revoke("Ban1001", Body("{\"actor\":\"Mod Two\",\"note\":\"appeal\"}"));
        _repository.Verify(r => r.CreateBan(It.IsAny<JsonObject>(), "Mod One", admin));
        _repository.Verify(r => r.EditBan("Ban1001", It.IsAny<JsonObject>(), "Mod One", admin));
        _repository.Verify(r => r.RevokeBan("Ban1001", It.IsAny<JsonObject>(), "Mod One", admin));
    }

    [Test]
    public async Task MachineRoutes_Answer403WithoutAdminModeration_BeforeCallingMatchmaking()
    {
        AsUser(EPermission.Moderation);
        foreach (var result in new[] { await _controller.MachineIds("1001"),
                                       await _controller.MachineIdAccounts("m1001", "1001"),
                                       await _controller.MachineImpact(Body("{\"accountKey\":\"1001\",\"machineIds\":[\"m1001\"]}")) })
        {
            Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(403));
            Assert.That(ErrorCode(result), Is.EqualTo("MACHINE_BAN_PERMISSION"));
            Assert.That(JObject.FromObject(((ObjectResult)result).Value!)["errors"]![0]!["msg"]!.Value<string>(),
                Is.EqualTo("Permission denied, please contact an Administrator"));
        }
        _repository.VerifyNoOtherCalls();
    }

    [Test]
    public async Task MachineRoutes_AreForwardedForAdminModeration()
    {
        AsUser(EPermission.Moderation, EPermission.AdminModeration);
        await _controller.MachineIds("1001");
        await _controller.MachineIdAccounts("m1001", "1001");
        await _controller.MachineImpact(Body("{\"accountKey\":\"1001\",\"machineIds\":[\"m1001\"]}"));
        _repository.Verify(r => r.GetBanAccountMachineIds("1001", true));
        _repository.Verify(r => r.GetBanMachineIdAccounts("m1001", "1001", true));
        _repository.Verify(r => r.GetBanMachineImpact(It.IsAny<JsonObject>(), true));
    }

    [TestCase(403, "{\"errors\":[{\"msg\":\"Permission denied, please contact an Administrator\",\"code\":\"MACHINE_BAN_PERMISSION\"}]}")]
    [TestCase(409, "{\"errors\":[{\"msg\":\"changed\",\"code\":\"BAN_VERSION_CONFLICT\"}]}")]
    [TestCase(409, "{\"errors\":[{\"msg\":\"ended\",\"code\":\"BAN_NOT_IN_EFFECT\"}]}")]
    [TestCase(201, "{\"id\":\"Ban1001\"}")]
    public async Task MatchmakingStatusAndBody_PassThroughUnchanged(int status, string body)
    {
        AsUser(EPermission.Moderation);
        _repository.Setup(r => r.EditBan("Ban1001", It.IsAny<JsonObject>(), "Mod One", false)).ReturnsAsync(new MatchmakingProxyResult(status, body));
        var result = (ContentResult)await _controller.Edit("Ban1001", Body("{\"version\":3}"));
        Assert.That(result.StatusCode, Is.EqualTo(status));
        Assert.That(result.Content, Is.EqualTo(body));
    }

    // ASP.NET Core leaves %2F encoded inside a route value and decodes everything else (the website's
    // "tag%3ATester%231001" arrives as "tag:Tester#1001"). The controller unescapes once, the repository encodes once.
    [TestCase("ab%2F12", "ab/12")]
    [TestCase("tag:Tester#1001", "tag:Tester#1001")]
    [TestCase("tag:Тестер#1002", "tag:Тестер#1002")]
    public async Task RouteValues_ReachTheRepositoryUnescaped(string routeValue, string expected)
    {
        AsUser(EPermission.Moderation, EPermission.AdminModeration);
        await _controller.Account(routeValue);
        await _controller.Edit(routeValue, Body("{\"version\":3}"));
        await _controller.Revoke(routeValue, Body("{\"note\":\"appeal\"}"));
        await _controller.MachineIds(routeValue);
        await _controller.MachineIdAccounts(routeValue, "1001");
        _repository.Verify(r => r.GetBanAccount(expected, true));
        _repository.Verify(r => r.EditBan(expected, It.IsAny<JsonObject>(), "Mod One", true));
        _repository.Verify(r => r.RevokeBan(expected, It.IsAny<JsonObject>(), "Mod One", true));
        _repository.Verify(r => r.GetBanAccountMachineIds(expected, true));
        _repository.Verify(r => r.GetBanMachineIdAccounts(expected, "1001", true));
    }

    [TestCase("")]
    [TestCase(".")]
    [TestCase("..")]
    [TestCase("%2E%2E")]   // ".." only after unescaping
    public async Task DotOrEmptySegments_Answer400_WithoutCallingMatchmaking(string segment)
    {
        AsUser(EPermission.Moderation, EPermission.AdminModeration);
        var results = new[] { await _controller.Account(segment), await _controller.Edit(segment, Body("{}")),
            await _controller.Revoke(segment, Body("{}")), await _controller.MachineIds(segment),
            await _controller.MachineIdAccounts(segment, "1001") };
        foreach (var result in results)
        {
            Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(400));
            Assert.That(ErrorCode(result), Is.EqualTo("VALIDATION"));
        }
        _repository.VerifyNoOtherCalls();
    }

    [Test]
    public async Task MissingBody_Answers400_WithoutCallingMatchmaking()
    {
        AsUser(EPermission.Moderation, EPermission.AdminModeration);
        var results = new[] { await _controller.Create(null), await _controller.Edit("Ban1001", null),
            await _controller.Revoke("Ban1001", null), await _controller.MachineImpact(null), await _controller.Status(null) };
        foreach (var result in results)
        {
            Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(400));
            Assert.That(ErrorCode(result), Is.EqualTo("VALIDATION"));
        }
        _repository.VerifyNoOtherCalls();
    }
}
