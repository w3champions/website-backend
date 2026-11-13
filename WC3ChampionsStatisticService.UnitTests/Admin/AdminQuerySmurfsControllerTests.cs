using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using W3C.Contracts.Admin.Permission;
using W3ChampionsStatisticService.Admin;
using W3ChampionsStatisticService.Admin.SmurfDetection;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace WC3ChampionsStatisticService.Tests.Admin;

[TestFixture]
public class AdminQuerySmurfsControllerTests
{
    private const string Crafted = "battleTag&generateExplanation=true&iterationDepth=9#frag";

    private Mock<IAdminRepository> _repository;
    private AdminController _controller;

    [SetUp]
    public void SetUp()
    {
        _repository = new Mock<IAdminRepository>();
        _controller = new AdminController(null, null, null, null, _repository.Object, null, null);
    }

    private void AsUser(params EPermission[] permissions)
    {
        var context = new DefaultHttpContext();
        context.Items[InjectActingPlayerFromAuthCodeFilter.ActingPlayerUserKey] =
            new W3CUserAuthenticationDto { BattleTag = "Mod#1", Permissions = permissions.ToHashSet() };
        _controller.ControllerContext = new ControllerContext { HttpContext = context };
    }

    [Test]
    public async Task QuerySmurfs_CraftedIdentifierType_ReachesTheRepositoryUnchanged_WithTheExplanationFlagFalse()
    {
        _repository.Setup(r => r.QuerySmurfsFor(Crafted, "Foo#1234", false, 1)).ReturnsAsync(new SmurfDetectionResult());
        AsUser(EPermission.SmurfCheckerQuery);

        var result = await _controller.QuerySmurfs(Crafted, "Foo#1234", false, 1);

        Assert.That(result, Is.TypeOf<OkObjectResult>());
        _repository.VerifyAll();
        _repository.VerifyNoOtherCalls();
    }

    [Test]
    public async Task QuerySmurfs_ExplanationWithoutThePermission_IsRefusedBeforeTheRepository()
    {
        AsUser(EPermission.SmurfCheckerQuery);

        var result = await _controller.QuerySmurfs("battleTag", "Foo#1234", true, 1);

        Assert.That(result, Is.TypeOf<UnauthorizedObjectResult>());
        _repository.VerifyNoOtherCalls();
    }
}
