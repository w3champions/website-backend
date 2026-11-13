using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

    [TestCase(nameof(SmurfEventsModerationController.List), EPermission.SmurfCheckerAdministration, false)]
    [TestCase(nameof(SmurfEventsModerationController.Get), EPermission.SmurfCheckerAdministration, false)]
    [TestCase(nameof(SmurfEventsModerationController.DenyPreview), EPermission.SmurfCheckerAdministration, false)]
    [TestCase(nameof(SmurfEventsModerationController.Preview), EPermission.SmurfCheckerAdministration, false)]
    [TestCase(nameof(SmurfEventsModerationController.Create), EPermission.SmurfCheckerAdministration, true)]
    [TestCase(nameof(SmurfEventsModerationController.Update), EPermission.SmurfCheckerAdministration, true)]
    [TestCase(nameof(SmurfEventsModerationController.Delete), EPermission.SmurfCheckerAdministration, true)]
    [TestCase(nameof(SmurfEventsModerationController.Review), EPermission.SmurfCheckerAdministration, true)]
    [TestCase(nameof(SmurfEventsModerationController.Rename), EPermission.SmurfCheckerAdministration, true)]
    [TestCase(nameof(SmurfEventsModerationController.Identifiers), EPermission.SmurfCheckerAdministration, true)]
    [TestCase(nameof(SmurfEventsModerationController.History), EPermission.SmurfCheckerAdministration, false)]
    public void Action_RequiresTheContractPermission(string action, EPermission permission, bool injectsActingPlayer)
    {
        var method = typeof(SmurfEventsModerationController).GetMethod(action)!;
        var filter = method.GetCustomAttribute<BearerHasPermissionFilter>();

        Assert.That(filter, Is.Not.Null);
        Assert.That(filter!.Permission, Is.EqualTo(permission));
        // Reads and Preview need the administration permission but write nothing, so they have no author to inject.
        Assert.That(method.GetCustomAttribute<InjectActingPlayerAuthCodeAttribute>() != null, Is.EqualTo(injectsActingPlayer));
    }

    [Test]
    public async Task BusyAnswer_OnPreviewCreateAndUpdate_ReachesTheCallerAs429WithItsBody()
    {
        const string body = "{\"error\":\"placeholder busy message\"}";
        var busy = new MatchmakingProxyResult(429, body);
        _repository.Setup(r => r.PreviewSmurfEvent(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>())).ReturnsAsync(busy);
        _repository.Setup(r => r.CreateSmurfEvent(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(busy);
        _repository.Setup(r => r.UpdateSmurfEvent(Id, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(busy);

        var results = new[]
        {
            await _controller.Preview(Body()),
            await _controller.Create(Body(), "Mod#1"),
            await _controller.Update(Id, Body(), "Mod#1"),
        };

        foreach (var result in results)
        {
            var content = result as ContentResult;
            Assert.That(content, Is.Not.Null);
            Assert.That(content!.StatusCode, Is.EqualTo(429));
            Assert.That(content.Content, Is.EqualTo(body));
        }
    }

    [Test]
    public void Controller_OnlyExposesTheElevenContractActions()
    {
        var actions = typeof(SmurfEventsModerationController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.That(actions, Has.Length.EqualTo(11));
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
            "GET ", "GET {id}", "GET {id}/deny-preview", "POST preview", "POST ", "PUT {id}", "DELETE {id}", "POST {id}/review", "POST {id}/rename", "GET {id}/identifiers", "GET {id}/history",
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
        foreach (var type in new[] { typeof(SmurfEventBody), typeof(SmurfEventReviewBody), typeof(SmurfEventRenameBody), typeof(SmurfReasonBody) })
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
        _repository.Setup(r => r.PreviewSmurfEvent(b.Name, b.From, b.To, b.Anchors, null)).ReturnsAsync(Ok200);

        await _controller.Preview(b);

        _repository.VerifyAll();
    }

    [Test]
    public async Task Preview_ForwardsTheEventItReplaces()
    {
        var b = Body();
        b.Replacing = "507f1f77bcf86cd799439011";
        _repository.Setup(r => r.PreviewSmurfEvent(b.Name, b.From, b.To, b.Anchors, "507f1f77bcf86cd799439011")).ReturnsAsync(Ok200);

        await _controller.Preview(b);

        _repository.VerifyAll();
    }

    [TestCase("")]
    [TestCase("a/b")]
    [TestCase("ev-1\n")]
    public async Task Preview_Answers400ForAMalformedReplacing_WithoutAnUpstreamCall(string replacing)
    {
        var b = Body();
        b.Replacing = replacing;

        AssertBadRequest(await _controller.Preview(b), "replacing");
        _repository.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ConflictOnUpdate_AndBusyOnReview_ReachTheCallerUnchanged()
    {
        const string conflict = "{\"error\":\"placeholder conflict\"}";
        const string busy = "{\"error\":\"placeholder busy\"}";
        _repository.Setup(r => r.UpdateSmurfEvent(Id, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(new MatchmakingProxyResult(409, conflict));
        _repository.Setup(r => r.ReviewSmurfEvent(Id, "denied", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(new MatchmakingProxyResult(429, busy));

        var put = await _controller.Update(Id, Body(), "Mod#1") as ContentResult;
        var review = await _controller.Review(Id, new SmurfEventReviewBody { Decision = "denied" }, "Mod#1") as ContentResult;

        Assert.That((put!.StatusCode, put.Content), Is.EqualTo((409, conflict)));
        Assert.That((review!.StatusCode, review.Content), Is.EqualTo((429, busy)));
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
        _repository.Setup(r => r.ReviewSmurfEvent(Id, "denied", "r", null, "Mod#1")).ReturnsAsync(Ok200);

        await _controller.Review(Id, new SmurfEventReviewBody { Decision = "denied", Reason = " r " }, "Mod#1");

        _repository.VerifyAll();
    }

    [Test]
    public async Task Review_PassesTheTrimmedNameThrough_AndNullWhenBlank()
    {
        _repository.Setup(r => r.ReviewSmurfEvent(Id, "confirmed", null, "Sofia LAN", "Mod#1")).ReturnsAsync(Ok200);
        _repository.Setup(r => r.ReviewSmurfEvent(Id, "denied", null, null, "Mod#1")).ReturnsAsync(Ok200);

        await _controller.Review(Id, new SmurfEventReviewBody { Decision = "confirmed", Name = "  Sofia LAN " }, "Mod#1");
        await _controller.Review(Id, new SmurfEventReviewBody { Decision = "denied", Name = "   " }, "Mod#1");

        _repository.VerifyAll();
    }

    [Test]
    public async Task Rename_UsesTheActingPlayerAsAuthor_AndPassesTheTrimmedNameAndReason()
    {
        var longName = new string('x', 500);
        _repository.Setup(r => r.RenameSmurfEvent(Id, "Sofia LAN", "typo", "Mod#1")).ReturnsAsync(Ok200);
        _repository.Setup(r => r.RenameSmurfEvent(Id, longName, null, "Mod#1")).ReturnsAsync(Ok200);

        await _controller.Rename(Id, new SmurfEventRenameBody { Name = " Sofia LAN ", Reason = " typo " }, "Mod#1");
        await _controller.Rename(Id, new SmurfEventRenameBody { Name = longName }, "Mod#1");

        _repository.VerifyAll();
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("\t\n")]
    [TestCase(null)]
    public async Task Rename_Answers400ForABlankName_WithoutCallingMatchmaking(string name)
    {
        AssertBadRequest(await _controller.Rename(Id, new SmurfEventRenameBody { Name = name, Reason = "typo" }, "Mod#1"), "name");
        _repository.VerifyNoOtherCalls();
    }

    [TestCase(nameof(SmurfEventsModerationController.Rename))]
    [TestCase(nameof(SmurfEventsModerationController.Review))]
    public void RenameAndReview_CarryTheTransportBodyLimit(string action)
    {
        var limit = typeof(SmurfEventsModerationController).GetMethod(action)!.GetCustomAttribute<RequestSizeLimitAttribute>();

        Assert.That(limit, Is.Not.Null);
        Assert.That(((IRequestSizeLimitMetadata)limit!).MaxRequestBodySize, Is.EqualTo(SmurfEventsModerationController.MaxJsonBodyBytes));
    }

    [Test]
    public async Task Rename_Answers400ForANullBodyOrABadId()
    {
        AssertBadRequest(await _controller.Rename(Id, null, "Mod#1"), "body");
        AssertBadRequest(await _controller.Rename("a/b", new SmurfEventRenameBody { Name = "n" }, "Mod#1"), "id");
        _repository.VerifyNoOtherCalls();
    }

    private void AsUser(params EPermission[] permissions)
    {
        var context = new DefaultHttpContext();
        context.Items[InjectActingPlayerFromAuthCodeFilter.ActingPlayerUserKey] =
            new W3CUserAuthenticationDto { BattleTag = "Mod#1", Permissions = permissions.ToHashSet() };
        _controller.ControllerContext = new ControllerContext { HttpContext = context };
    }

    [Test]
    public async Task Identifiers_Reveals_OnlyForACallerWithTheAdministrationPermission()
    {
        _repository.Setup(r => r.GetSmurfEventIdentifiers(Id, true)).ReturnsAsync(Ok200);
        AsUser(EPermission.SmurfCheckerAdministration);

        await _controller.Identifiers(Id);

        _repository.VerifyAll();
    }

    [Test]
    public async Task Identifiers_DoesNotReveal_ForACallerWithoutIt()
    {
        _repository.Setup(r => r.GetSmurfEventIdentifiers(Id, false)).ReturnsAsync(Ok200);
        AsUser(EPermission.SmurfCheckerQuery);

        await _controller.Identifiers(Id);

        _repository.VerifyAll();
    }

    [Test]
    public async Task Identifiers_WithNoPermissionsAtAll_DoesNotReveal()
    {
        _repository.Setup(r => r.GetSmurfEventIdentifiers(Id, false)).ReturnsAsync(Ok200);
        var context = new DefaultHttpContext();
        context.Items[InjectActingPlayerFromAuthCodeFilter.ActingPlayerUserKey] = new W3CUserAuthenticationDto { BattleTag = "Mod#1" };
        _controller.ControllerContext = new ControllerContext { HttpContext = context };

        await _controller.Identifiers(Id);

        _repository.VerifyAll();
    }

    [Test]
    public void Identifiers_HasNoRevealParameter()
    {
        var parameters = typeof(SmurfEventsModerationController).GetMethod(nameof(SmurfEventsModerationController.Identifiers))!.GetParameters();

        Assert.That(parameters.Select(p => p.Name), Is.EqualTo(new[] { "id" }));
    }

    [Test]
    public async Task History_Forwards()
    {
        _repository.Setup(r => r.GetSmurfEventHistory(Id)).ReturnsAsync(Ok200);

        Assert.That((await _controller.History(Id) as ContentResult)!.StatusCode, Is.EqualTo(200));
        _repository.VerifyAll();
    }

    [Test]
    public async Task Identifiers_And_History_Answer400ForABadId()
    {
        AsUser(EPermission.SmurfCheckerAdministration);

        AssertBadRequest(await _controller.Identifiers("a/b"), "id");
        AssertBadRequest(await _controller.History("a/b"), "id");
        _repository.VerifyNoOtherCalls();
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
        AssertBadRequest(await _controller.Rename(id, new SmurfEventRenameBody { Name = "n" }, "Mod#1"), "id");
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
