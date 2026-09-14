using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Moq;
using NUnit.Framework;
using W3C.Contracts.Admin.Permission;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using static WC3ChampionsStatisticService.Tests.AuthFilterTestHelper;

namespace WC3ChampionsStatisticService.Tests.Auth;

[TestFixture]
public class CheckIfBattleTagIsAdminFilterTests
{
    [Test]
    public async Task Admin_InvokesNext_AndInjectsBattleTag()
    {
        var filter = new CheckIfBattleTagIsAdminFilter(AuthReturning("admin#123", isAdmin: true).Object);
        var (context, invoked) = CreateActionExecutingContext("Bearer good-token");

        await filter.OnActionExecutionAsync(context, NextDelegate(invoked, context));

        Assert.That(invoked[0], Is.True);
        Assert.That(context.Result, Is.Null);
        Assert.That(context.ActionArguments["battleTag"], Is.EqualTo("admin#123"));
    }

    [Test]
    public async Task NonAdmin_Returns401_NextNotInvoked()
    {
        var filter = new CheckIfBattleTagIsAdminFilter(AuthReturning("peter#123", isAdmin: false).Object);
        var (context, invoked) = CreateActionExecutingContext("Bearer good-token");

        await filter.OnActionExecutionAsync(context, NextDelegate(invoked, context));

        Assert.That(invoked[0], Is.False);
        AssertUnauthorizedWithError(context.Result, LegacyUnauthorizedError);
    }

    [Test]
    public async Task AdminTokenWithoutBattleTag_Returns401_NextNotInvoked()
    {
        var filter = new CheckIfBattleTagIsAdminFilter(AuthReturning("", isAdmin: true).Object);
        var (context, invoked) = CreateActionExecutingContext("Bearer good-token");

        await filter.OnActionExecutionAsync(context, NextDelegate(invoked, context));

        Assert.That(invoked[0], Is.False);
        AssertUnauthorizedWithError(context.Result, LegacyUnauthorizedError);
    }

    [TestCase(null)]
    [TestCase("Basic aGk6dGhlcmU=")]
    public async Task MissingOrNonBearerAuthorizationHeader_Returns401(string authorizationHeader)
    {
        var auth = new Mock<IW3CAuthenticationService>(MockBehavior.Strict);
        var filter = new CheckIfBattleTagIsAdminFilter(auth.Object);
        var (context, invoked) = CreateActionExecutingContext(authorizationHeader);

        await filter.OnActionExecutionAsync(context, NextDelegate(invoked, context));

        Assert.That(invoked[0], Is.False);
        AssertUnauthorizedWithError(context.Result, LegacyUnauthorizedError);
        auth.Verify(a => a.GetUserByToken(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Test]
    public async Task ExpiredToken_KeepsItsDedicatedTokenExpiredResponse()
    {
        var auth = new Mock<IW3CAuthenticationService>();
        auth.Setup(a => a.GetUserByToken(It.IsAny<string>(), It.IsAny<bool>()))
            .Throws(new SecurityTokenExpiredException("expired"));
        var filter = new CheckIfBattleTagIsAdminFilter(auth.Object);
        var (context, invoked) = CreateActionExecutingContext("Bearer stale");

        await filter.OnActionExecutionAsync(context, NextDelegate(invoked, context));

        // The website admin panel keys its re-login prompt off this exact body.
        Assert.That(invoked[0], Is.False);
        Assert.That(context.Result, Is.TypeOf<UnauthorizedObjectResult>());
        var body = ((UnauthorizedObjectResult)context.Result).Value;
        Assert.That(PropertyOf(body, "StatusCode"), Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(PropertyOf(body, "Error"), Is.EqualTo("AUTH_TOKEN_EXPIRED"));
        Assert.That(PropertyOf(body, "Message"), Is.EqualTo("Token expired."));
    }

    private static object PropertyOf(object anonymousBody, string name) =>
        anonymousBody.GetType().GetProperty(name)?.GetValue(anonymousBody);

    private static Mock<IW3CAuthenticationService> AuthReturning(string battleTag, bool isAdmin)
    {
        // Admin paths validate lifetime; a call with `false` returns null from the loose mock and fails
        // the admin test.
        var auth = new Mock<IW3CAuthenticationService>();
        auth.Setup(a => a.GetUserByToken("good-token", true))
            .Returns(new W3CUserAuthenticationDto
            {
                BattleTag = battleTag,
                Name = "someone",
                IsAdmin = isAdmin,
                Permissions = new HashSet<EPermission>(),
            });
        return auth;
    }
}
