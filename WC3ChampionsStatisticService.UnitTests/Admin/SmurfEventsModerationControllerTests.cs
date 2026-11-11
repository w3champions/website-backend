using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
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
public class SmurfEventsModerationControllerTests
{
    private const string Id = "ev-1";
    private static readonly MatchmakingProxyResult Ok200 = new(200, "{\"ok\":true}");

    private Mock<IAdminRepository> _repository;
    private SmurfEventsModerationController _controller;

    [SetUp]
    public void SetUp()
    {
        _repository = new Mock<IAdminRepository>();
        _controller = new SmurfEventsModerationController(_repository.Object);
    }

    private static SmurfEventBody Body(string reason = "why") => new()
    {
        Name = "Name",
        From = "2026-01-01T00:00:00.000Z",
        To = "2026-01-02T00:00:00.000Z",
        Anchors = ["PlayerA#1234", "PlayerB#5678"],
        Reason = reason,
    };

    private static void AssertBadRequest(IActionResult result, string field)
    {
        var bad = result as ObjectResult;
        Assert.That(bad, Is.Not.Null);
        Assert.That(bad!.StatusCode, Is.EqualTo(400));
        Assert.That(JObject.FromObject(bad.Value!)["field"]!.Value<string>(), Is.EqualTo(field));
    }

    [TestCase(nameof(SmurfEventsModerationController.List), EPermission.SmurfCheckerQuery)]
    [TestCase(nameof(SmurfEventsModerationController.Get), EPermission.SmurfCheckerQuery)]
    [TestCase(nameof(SmurfEventsModerationController.DenyPreview), EPermission.SmurfCheckerQuery)]
    [TestCase(nameof(SmurfEventsModerationController.Preview), EPermission.SmurfCheckerQuery)]
    [TestCase(nameof(SmurfEventsModerationController.Create), EPermission.SmurfCheckerAdministration)]
    [TestCase(nameof(SmurfEventsModerationController.Update), EPermission.SmurfCheckerAdministration)]
    [TestCase(nameof(SmurfEventsModerationController.Delete), EPermission.SmurfCheckerAdministration)]
    [TestCase(nameof(SmurfEventsModerationController.Review), EPermission.SmurfCheckerAdministration)]
    public void Action_RequiresTheContractPermission(string action, EPermission permission)
    {
        var method = typeof(SmurfEventsModerationController).GetMethod(action)!;
        var filter = method.GetCustomAttribute<BearerHasPermissionFilter>();

        Assert.That(filter, Is.Not.Null);
        Assert.That(filter!.Permission, Is.EqualTo(permission));
        var isWrite = permission == EPermission.SmurfCheckerAdministration;
        Assert.That(method.GetCustomAttribute<InjectActingPlayerAuthCodeAttribute>() != null, Is.EqualTo(isWrite));
    }

    [Test]
    public void Controller_OnlyExposesTheEightContractActions()
    {
        var actions = typeof(SmurfEventsModerationController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.That(actions, Has.Length.EqualTo(8));
    }

    [Test]
    public void Actions_ExposeExactlyTheContractVerbAndTemplatePairs()
    {
        var pairs = typeof(SmurfEventsModerationController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>())
            .Select(a => $"{string.Join("|", a.HttpMethods)} {a.Template}");

        Assert.That(pairs, Is.EquivalentTo(new[]
        {
            "GET ", "GET {id}", "GET {id}/deny-preview", "POST preview", "POST ", "PUT {id}", "DELETE {id}", "POST {id}/review",
        }));
    }

    [Test]
    public void Delete_AcceptsARequestWithoutABody()
    {
        var body = typeof(SmurfEventsModerationController).GetMethod(nameof(SmurfEventsModerationController.Delete))!
            .GetParameters()[1].GetCustomAttribute<FromBodyAttribute>();

        Assert.That(body!.EmptyBodyBehavior, Is.EqualTo(EmptyBodyBehavior.Allow));
    }

    [Test]
    public void Routes_AreUnderTheEventsPath_AndNeverBindABattleTagValue()
    {
        var route = typeof(SmurfEventsModerationController).GetCustomAttribute<RouteAttribute>();
        Assert.That(route!.Template, Is.EqualTo("api/admin/smurf-detection/events"));

        var templates = typeof(SmurfEventsModerationController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>())
            .Select(a => a.Template ?? "");
        Assert.That(templates, Has.None.Contain("battleTag"));
    }

    [Test]
    public void Bodies_HaveNoAuthorMember_SoAClientAuthorCannotBind()
    {
        foreach (var type in new[] { typeof(SmurfEventBody), typeof(SmurfEventReviewBody), typeof(SmurfReasonBody) })
            Assert.That(type.GetProperties().Select(p => p.Name.ToLowerInvariant()), Does.Not.Contain("author"), type.Name);
    }

    [Test]
    public async Task List_ForwardsFilters_AndPassesStatusAndBodyThrough()
    {
        _repository.Setup(r => r.GetSmurfEvents("unreviewed", "manual", "c", 20)).ReturnsAsync(new MatchmakingProxyResult(404, "{\"error\":\"x\"}"));

        var result = await _controller.List("unreviewed", "manual", "c", 20) as ContentResult;

        Assert.That(result!.StatusCode, Is.EqualTo(404));
        Assert.That(result.Content, Is.EqualTo("{\"error\":\"x\"}"));
        Assert.That(result.ContentType, Does.StartWith("application/json"));
    }

    [TestCase(0)]
    [TestCase(101)]
    public async Task List_Answers400ForALimitOutsideTheContract(int limit)
    {
        AssertBadRequest(await _controller.List(null, null, null, limit), "limit");
        _repository.VerifyNoOtherCalls();
    }

    [TestCase(1)]
    [TestCase(100)]
    public async Task List_AcceptsTheLimitBounds(int limit)
    {
        _repository.Setup(r => r.GetSmurfEvents(null, null, null, limit)).ReturnsAsync(Ok200);

        Assert.That((await _controller.List(null, null, null, limit) as ContentResult)!.StatusCode, Is.EqualTo(200));
    }

    [Test]
    public async Task UnavailableResult_IsForwardedAs502()
    {
        _repository.Setup(r => r.GetSmurfEvent(Id)).ReturnsAsync(MatchmakingProxyResult.Unavailable());

        var result = await _controller.Get(Id) as ContentResult;

        Assert.That(result!.StatusCode, Is.EqualTo(502));
    }

    [Test]
    public async Task Get_And_DenyPreview_Forward()
    {
        _repository.Setup(r => r.GetSmurfEvent(Id)).ReturnsAsync(Ok200);
        _repository.Setup(r => r.GetSmurfEventDenyPreview(Id)).ReturnsAsync(Ok200);

        Assert.That((await _controller.Get(Id) as ContentResult)!.StatusCode, Is.EqualTo(200));
        Assert.That((await _controller.DenyPreview(Id) as ContentResult)!.StatusCode, Is.EqualTo(200));
        _repository.VerifyAll();
    }

    [Test]
    public async Task Preview_Forwards()
    {
        var b = Body();
        _repository.Setup(r => r.PreviewSmurfEvent(b.Name, b.From, b.To, b.Anchors)).ReturnsAsync(Ok200);

        await _controller.Preview(b);

        _repository.VerifyAll();
    }

    [Test]
    public async Task Create_UsesTheActingPlayerAsAuthor_AndTrimsTheReason()
    {
        var b = Body("  why ");
        _repository.Setup(r => r.CreateSmurfEvent(b.Name, b.From, b.To, b.Anchors, "why", "Mod#1")).ReturnsAsync(new MatchmakingProxyResult(201, "{}"));

        var result = await _controller.Create(b, "Mod#1") as ContentResult;

        Assert.That(result!.StatusCode, Is.EqualTo(201));
        _repository.VerifyAll();
    }

    [Test]
    public async Task Update_UsesTheActingPlayerAsAuthor()
    {
        var b = Body(null);
        _repository.Setup(r => r.UpdateSmurfEvent(Id, b.Name, b.From, b.To, b.Anchors, null, "Mod#1")).ReturnsAsync(Ok200);

        await _controller.Update(Id, b, "Mod#1");

        _repository.VerifyAll();
    }

    [Test]
    public async Task Delete_UsesTheActingPlayerAsAuthor_AndAcceptsNoBody()
    {
        _repository.Setup(r => r.DeleteSmurfEvent(Id, null, "Mod#1")).ReturnsAsync(new MatchmakingProxyResult(204, ""));

        var result = await _controller.Delete(Id, null, "Mod#1") as ContentResult;

        Assert.That(result!.StatusCode, Is.EqualTo(204));
        _repository.VerifyAll();
    }

    [Test]
    public async Task Review_UsesTheActingPlayerAsAuthor()
    {
        _repository.Setup(r => r.ReviewSmurfEvent(Id, "denied", "r", "Mod#1")).ReturnsAsync(Ok200);

        await _controller.Review(Id, new SmurfEventReviewBody { Decision = "denied", Reason = " r " }, "Mod#1");

        _repository.VerifyAll();
    }

    [TestCase("")]
    [TestCase("a/b")]
    [TestCase("..")]
    [TestCase(".")]
    [TestCase("ev-1\n")]
    [TestCase("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // 65 characters
    public async Task BadId_Answers400OnEveryIdRoute(string id)
    {
        AssertBadRequest(await _controller.Get(id), "id");
        AssertBadRequest(await _controller.DenyPreview(id), "id");
        AssertBadRequest(await _controller.Update(id, Body(), "Mod#1"), "id");
        AssertBadRequest(await _controller.Delete(id, null, "Mod#1"), "id");
        AssertBadRequest(await _controller.Review(id, new SmurfEventReviewBody { Decision = "denied" }, "Mod#1"), "id");
        _repository.VerifyNoOtherCalls();
    }

    [TestCase("507f1f77bcf86cd799439011")] // the 24 hex characters matchmaking generates
    [TestCase("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // 64 characters
    public async Task GoodId_IsForwarded(string id)
    {
        _repository.Setup(r => r.GetSmurfEvent(id)).ReturnsAsync(Ok200);

        Assert.That((await _controller.Get(id) as ContentResult)!.StatusCode, Is.EqualTo(200));
    }

    [Test]
    public async Task MissingBody_OnADirectCall_Answers400()
    {
        AssertBadRequest(await _controller.Preview(null), "body");
        AssertBadRequest(await _controller.Create(null, "Mod#1"), "body");
        AssertBadRequest(await _controller.Update(Id, null, "Mod#1"), "body");
        AssertBadRequest(await _controller.Review(Id, null, "Mod#1"), "body");
        _repository.VerifyNoOtherCalls();
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("maybe")]
    public async Task Review_Answers400ForAnUnknownDecision(string decision)
    {
        AssertBadRequest(await _controller.Review(Id, new SmurfEventReviewBody { Decision = decision }, "Mod#1"), "decision");
        _repository.VerifyNoOtherCalls();
    }
}
