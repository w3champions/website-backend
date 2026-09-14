using System.Threading.Tasks;
using NUnit.Framework;
using W3C.Contracts.Admin.Permission;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using static WC3ChampionsStatisticService.Tests.AuthFilterTestHelper;

namespace WC3ChampionsStatisticService.Tests.Auth;

[TestFixture]
public class BearerHasPermissionFilterTests
{
    // The filter constructs the real W3CAuthenticationService itself, so these requests exercise real
    // IdentityModel failures whose messages (IDX codes, token fragments) must never reach the response body.
    [TestCase(null)]
    [TestCase("Basic aGk6dGhlcmU=")]
    [TestCase("Bearer garbage")] // SecurityTokenMalformedException
    [TestCase("Bearer a.b.c")] // ArgumentException (IDX12729) from the JWT decoder
    public async Task RejectedRequest_Returns401_WithAFixedBody_AndNoExceptionText(string authorizationHeader)
    {
        var filter = new BearerHasPermissionFilter { Permission = EPermission.Moderation };
        var (context, invoked) = CreateActionExecutingContext(authorizationHeader);

        await filter.OnActionExecutionAsync(context, NextDelegate(invoked, context));

        Assert.That(invoked[0], Is.False);
        AssertUnauthorizedWithError(context.Result, "Unauthorized");
    }
}
