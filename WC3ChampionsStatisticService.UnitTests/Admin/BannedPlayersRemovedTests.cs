using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NUnit.Framework;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Admin;

namespace WC3ChampionsStatisticService.Tests.Admin;

[TestFixture]
public class BannedPlayersRemovedTests
{
    [Test]
    public void NoControllerRoute_ServesBannedPlayers()
    {
        var controllers = typeof(AdminController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t))
            .ToList();
        var classTemplates = controllers
            .SelectMany(t => t.GetCustomAttributes<RouteAttribute>(inherit: true))
            .Select(a => a.Template ?? "");
        var actionTemplates = controllers
            .SelectMany(t => t.GetMethods())
            .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>().Select(a => a.Template)
                .Concat(m.GetCustomAttributes<RouteAttribute>().Select(a => a.Template)))
            .Select(template => template ?? "");
        Assert.That(classTemplates, Is.Not.Empty);
        Assert.That(classTemplates.Concat(actionTemplates), Has.None.Contains("bannedPlayers").IgnoreCase);
    }

    [TestCase("BannedPlayerReadmodel")]
    [TestCase("BannedPlayerResponse")]
    [TestCase("BannedPlayersGetRequest")]
    [TestCase("UserVisibleBanReason")]
    public void OldBanReadModels_AreGone(string typeName) =>
        Assert.That(typeof(MatchmakingServiceClient).Assembly.GetType($"W3C.Domain.MatchmakingService.{typeName}"), Is.Null);
}
