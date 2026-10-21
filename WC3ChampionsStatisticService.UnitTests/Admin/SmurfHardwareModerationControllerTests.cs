using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using W3C.Contracts.Admin.Permission;
using W3ChampionsStatisticService.Admin;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace WC3ChampionsStatisticService.Tests.Admin;

[TestFixture]
public class SmurfHardwareModerationControllerTests
{
    private const string Tag = "Name#1234";
    private const string Other = "Other#9999";
    private const string Hash = "0a1b2c3d";

    private Mock<IAdminRepository> _repository;
    private SmurfHardwareModerationController _controller;

    [SetUp]
    public void SetUp()
    {
        _repository = new Mock<IAdminRepository>();
        _controller = new SmurfHardwareModerationController(_repository.Object);
    }

    private static readonly MatchmakingProxyResult Ok200 = new(200, "{\"ok\":true}");

    private static void AssertBadRequest(IActionResult result, string field)
    {
        var bad = result as ObjectResult;
        Assert.That(bad, Is.Not.Null);
        Assert.That(bad!.StatusCode, Is.EqualTo(400));
        Assert.That(JObject.FromObject(bad.Value!)["field"]!.Value<string>(), Is.EqualTo(field));
    }

    // Permission table of the contract.
    [TestCase(nameof(SmurfHardwareModerationController.GetLinks), EPermission.SmurfCheckerQuery)]
    [TestCase(nameof(SmurfHardwareModerationController.GetItems), EPermission.SmurfCheckerQuery)]
    [TestCase(nameof(SmurfHardwareModerationController.GetItem), EPermission.SmurfCheckerQuery)]
    [TestCase(nameof(SmurfHardwareModerationController.GetAudit), EPermission.SmurfCheckerQuery)]
    [TestCase(nameof(SmurfHardwareModerationController.SetSmurf), EPermission.SmurfCheckerAdministration)]
    [TestCase(nameof(SmurfHardwareModerationController.RevokeSmurf), EPermission.SmurfCheckerAdministration)]
    [TestCase(nameof(SmurfHardwareModerationController.SetNotSmurf), EPermission.SmurfCheckerAdministration)]
    [TestCase(nameof(SmurfHardwareModerationController.RevokeNotSmurf), EPermission.SmurfCheckerAdministration)]
    [TestCase(nameof(SmurfHardwareModerationController.TrustItem), EPermission.SmurfCheckerAdministration)]
    [TestCase(nameof(SmurfHardwareModerationController.RevokeTrust), EPermission.SmurfCheckerAdministration)]
    [TestCase(nameof(SmurfHardwareModerationController.IgnoreItem), EPermission.SmurfCheckerAdministration)]
    [TestCase(nameof(SmurfHardwareModerationController.RevokeIgnore), EPermission.SmurfCheckerAdministration)]
    public void Action_RequiresTheContractPermission(string action, EPermission permission)
    {
        var method = typeof(SmurfHardwareModerationController).GetMethod(action)!;
        var filter = method.GetCustomAttribute<BearerHasPermissionFilter>();

        Assert.That(filter, Is.Not.Null);
        Assert.That(filter!.Permission, Is.EqualTo(permission));
        var isWrite = permission == EPermission.SmurfCheckerAdministration;
        Assert.That(method.GetCustomAttribute<InjectActingPlayerAuthCodeAttribute>() != null, Is.EqualTo(isWrite));
    }

    [Test]
    public void Controller_OnlyExposesTheTwelveContractActions()
    {
        var actions = typeof(SmurfHardwareModerationController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.That(actions, Has.Length.EqualTo(12));
    }

    [Test]
    public async Task GetLinks_PassesStatusAndBodyThrough()
    {
        _repository.Setup(r => r.GetHardwareLinks(Tag)).ReturnsAsync(new MatchmakingProxyResult(404, "{\"error\":\"x\"}"));

        var result = await _controller.GetLinks(Tag) as ContentResult;

        Assert.That(result!.StatusCode, Is.EqualTo(404));
        Assert.That(result.Content, Is.EqualTo("{\"error\":\"x\"}"));
        Assert.That(result.ContentType, Does.StartWith("application/json"));
    }

    [Test]
    public async Task Mm403_IsAnswered502()
    {
        _repository.Setup(r => r.GetHardwareItems(Tag)).ReturnsAsync(MatchmakingProxyResult.Unavailable());

        var result = await _controller.GetItems(Tag) as ContentResult;

        Assert.That(result!.StatusCode, Is.EqualTo(502));
    }

    [Test]
    public async Task SetSmurf_UsesTheActingPlayerAsAuthor_AndTrimsTheReason()
    {
        _repository.Setup(r => r.SetSmurfVerdict(Tag, Other, "why", "Mod#1")).ReturnsAsync(Ok200);

        var result = await _controller.SetSmurf(Tag, Other, new SmurfReasonBody { Reason = "  why " }, "Mod#1") as ContentResult;

        Assert.That(result!.StatusCode, Is.EqualTo(200));
        _repository.VerifyAll();
    }

    [Test]
    public void ReasonBody_HasNoAuthorMember_SoAClientAuthorCannotBind()
    {
        Assert.That(typeof(SmurfReasonBody).GetProperties().Select(p => p.Name.ToLowerInvariant()), Does.Not.Contain("author"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task WritesWithReason_Answer400ForAnEmptyReason(string reason)
    {
        var body = reason == null ? null : new SmurfReasonBody { Reason = reason };

        AssertBadRequest(await _controller.SetSmurf(Tag, Other, body, "Mod#1"), "reason");
        AssertBadRequest(await _controller.SetNotSmurf(Tag, Other, body, "Mod#1"), "reason");
        AssertBadRequest(await _controller.TrustItem(Tag, Hash, body, "Mod#1"), "reason");
        AssertBadRequest(await _controller.IgnoreItem(Tag, Hash, body, "Mod#1"), "reason");
        _repository.VerifyNoOtherCalls();
    }

    [TestCase("")]
    [TestCase("0A1B2C3D")]
    [TestCase("0a1b2c3")]
    [TestCase("0a1b2c3de")]
    [TestCase("0a1b2c3g")]
    [TestCase("../etc")]
    public async Task BadHash8_Answers400OnEveryItemRoute(string hash)
    {
        var body = new SmurfReasonBody { Reason = "r" };

        AssertBadRequest(await _controller.GetItem(Tag, hash), "hash8");
        AssertBadRequest(await _controller.TrustItem(Tag, hash, body, "Mod#1"), "hash8");
        AssertBadRequest(await _controller.RevokeTrust(Tag, hash, "Mod#1"), "hash8");
        AssertBadRequest(await _controller.IgnoreItem(Tag, hash, body, "Mod#1"), "hash8");
        AssertBadRequest(await _controller.RevokeIgnore(Tag, hash, "Mod#1"), "hash8");
        _repository.VerifyNoOtherCalls();
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(201)]
    public async Task BadLimit_Answers400(int limit)
    {
        AssertBadRequest(await _controller.GetAudit(null, null, limit), "limit");
        _repository.VerifyNoOtherCalls();
    }

    [TestCase(1)]
    [TestCase(200)]
    [TestCase(null)]
    public async Task LimitInRange_IsForwarded(int? limit)
    {
        _repository.Setup(r => r.GetHardwareAudit(Tag, "cursor", limit)).ReturnsAsync(Ok200);

        var result = await _controller.GetAudit(Tag, "cursor", limit) as ContentResult;

        Assert.That(result!.StatusCode, Is.EqualTo(200));
    }

    [TestCase("")]
    [TestCase("nohash")]
    [TestCase("Name#12")]
    [TestCase("a b#1234")]
    [TestCase("Name#1234/../x")]
    public async Task BadBattleTag_Answers400(string tag)
    {
        AssertBadRequest(await _controller.GetLinks(tag), "battleTag");
        AssertBadRequest(await _controller.GetItems(tag), "battleTag");
        AssertBadRequest(await _controller.RevokeSmurf(tag, Other, "Mod#1"), "battleTagA");
        AssertBadRequest(await _controller.RevokeSmurf(Tag, tag, "Mod#1"), "battleTagB");
        if (tag != "") AssertBadRequest(await _controller.GetAudit(tag, null, null), "battleTag");
        _repository.VerifyNoOtherCalls();
    }

    [Test]
    public async Task RevokeRoutes_ForwardTheActingPlayer()
    {
        _repository.Setup(r => r.RevokeHardwareItemIgnore(Tag, Hash, "Mod#1")).ReturnsAsync(Ok200);
        _repository.Setup(r => r.RevokeNotSmurfVerdict(Tag, Other, "Mod#1")).ReturnsAsync(Ok200);

        await _controller.RevokeIgnore(Tag, Hash, "Mod#1");
        await _controller.RevokeNotSmurf(Tag, Other, "Mod#1");

        _repository.VerifyAll();
    }
}
