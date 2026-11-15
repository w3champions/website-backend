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
        var templates = typeof(AdminController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods())
            .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>())
            .Select(a => a.Template ?? "");
        Assert.That(templates, Has.None.Contains("bannedPlayers"));
    }

    [TestCase("BannedPlayerReadmodel")]
    [TestCase("BannedPlayerResponse")]
    [TestCase("BannedPlayersGetRequest")]
    [TestCase("UserVisibleBanReason")]
    public void OldBanReadModels_AreGone(string typeName) =>
        Assert.That(typeof(MatchmakingServiceClient).Assembly.GetType($"W3C.Domain.MatchmakingService.{typeName}"), Is.Null);
}
