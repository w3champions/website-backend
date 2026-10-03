using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Tokens;
using Moq;
using NUnit.Framework;
using W3C.Contracts.Admin.Permission;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using static WC3ChampionsStatisticService.Tests.AuthFilterTestHelper;

namespace WC3ChampionsStatisticService.Tests.Auth;

[TestFixture]
public class BearerCheckIfBattleTagBelongsToAuthFilterTests
{
    private const string OwnBattleTag = "peter#123";

    [Test]
    public async Task MatchingBattleTag_InvokesNext_AndInjectsBattleTag()
    {
        var filter = new BearerCheckIfBattleTagBelongsToAuthFilter(AuthReturning(OwnBattleTag).Object);
        var (context, invoked) = CreateActionExecutingContext("Bearer good-token", OwnBattleTag);

        await filter.OnActionExecutionAsync(context, NextDelegate(invoked, context));

        Assert.That(invoked[0], Is.True);
        Assert.That(context.Result, Is.Null);
        Assert.That(context.ActionArguments["battleTag"], Is.EqualTo(OwnBattleTag));
    }

    [TestCase("someoneelse#456")]
    [TestCase(null)] // route carries no battleTag at all
    public async Task BattleTagNotBelongingToToken_Returns401_NextNotInvoked(string routeBattleTag)
    {
        var filter = new BearerCheckIfBattleTagBelongsToAuthFilter(AuthReturning(OwnBattleTag).Object);
        var (context, invoked) = CreateActionExecutingContext("Bearer good-token", routeBattleTag);

        await filter.OnActionExecutionAsync(context, NextDelegate(invoked, context));

        Assert.That(invoked[0], Is.False);
        AssertUnauthorizedWithError(context.Result, LegacyUnauthorizedError);
    }

    [TestCase(null)]
    [TestCase("Basic aGk6dGhlcmU=")]
    public async Task MissingOrNonBearerAuthorizationHeader_Returns401_NotA500(string authorizationHeader)
    {
        var auth = new Mock<IW3CAuthenticationService>(MockBehavior.Strict);
        var filter = new BearerCheckIfBattleTagBelongsToAuthFilter(auth.Object);
        var (context, invoked) = CreateActionExecutingContext(authorizationHeader, OwnBattleTag);

        await filter.OnActionExecutionAsync(context, NextDelegate(invoked, context));

        Assert.That(invoked[0], Is.False);
        AssertUnauthorizedWithError(context.Result, LegacyUnauthorizedError);
        auth.Verify(a => a.GetUserByToken(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Test]
    public async Task ThrowingAuthService_Returns401_NextNotInvoked()
    {
        var auth = new Mock<IW3CAuthenticationService>();
        auth.Setup(a => a.GetUserByToken(It.IsAny<string>(), It.IsAny<bool>()))
            .Throws(new SecurityTokenValidationException("bad signature"));
        var filter = new BearerCheckIfBattleTagBelongsToAuthFilter(auth.Object);
        var (context, invoked) = CreateActionExecutingContext("Bearer garbage", OwnBattleTag);

        await filter.OnActionExecutionAsync(context, NextDelegate(invoked, context));

        Assert.That(invoked[0], Is.False);
        AssertUnauthorizedWithError(context.Result, LegacyUnauthorizedError);
    }

    private static Mock<IW3CAuthenticationService> AuthReturning(string battleTag)
    {
        // Lifetime is deliberately not validated on this non-admin path; a call with `true` returns
        // null from the loose mock and fails the matching-battleTag test.
        var auth = new Mock<IW3CAuthenticationService>();
        auth.Setup(a => a.GetUserByToken("good-token", false))
            .Returns(new W3CUserAuthenticationDto
            {
                BattleTag = battleTag,
                Name = "peter",
                IsAdmin = false,
                Permissions = new HashSet<EPermission>(),
            });
        return auth;
    }
}
