using System.Linq;
using System.Reflection;
using NUnit.Framework;
using W3C.Contracts.Admin.Permission;
using W3ChampionsStatisticService.Admin;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace WC3ChampionsStatisticService.Tests.Admin;

[TestFixture]
public class AdminSmurfRoutePermissionTests
{
    [TestCase(nameof(AdminController.GetPossibleIdentifierTypes), EPermission.SmurfCheckerQuery)]
    [TestCase(nameof(AdminController.QuerySmurfs), EPermission.SmurfCheckerQuery)]
    [TestCase(nameof(AdminController.GetIgnoredIdentifiers), EPermission.SmurfCheckerAdministration)]
    [TestCase(nameof(AdminController.AddIgnoredIdentifier), EPermission.SmurfCheckerAdministration)]
    [TestCase(nameof(AdminController.DeleteIgnoredIdentifier), EPermission.SmurfCheckerAdministration)]
    public void SmurfAction_RequiresTheExpectedPermission(string action, EPermission permission)
    {
        // By name over all overloads, so a later overload cannot make the lookup ambiguous or go unchecked.
        var methods = typeof(AdminController).GetMethods().Where(m => m.Name == action).ToList();

        Assert.That(methods, Is.Not.Empty);
        foreach (var method in methods)
        {
            var filter = method.GetCustomAttribute<BearerHasPermissionFilter>();
            Assert.That(filter, Is.Not.Null);
            Assert.That(filter!.Permission, Is.EqualTo(permission));
        }
    }
}
