using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using Moq;
using NUnit.Framework;
using W3C.Contracts.Admin.Permission;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using W3ChampionsStatisticService.WebApi.ExceptionFilters;

namespace WC3ChampionsStatisticService.Tests.Auth;

// Hand-built ActionExecutingContext over a DefaultHttpContext, mirroring
// Friend/ChatServiceSecretAuthFilterTests.cs (no TestServer/WebApplicationFactory in this repo).
[TestFixture]
public class BearerCheckIfBattleTagBelongsToAuthFilterTests
{
    private const string OwnBattleTag = "peter#123";

    [Test]
    public async Task MatchingBattleTag_InvokesNext_AndInjectsBattleTag()
    {
        var filter = new BearerCheckIfBattleTagBelongsToAuthFilter(AuthReturning(OwnBattleTag).Object);
        var (context, invoked) = CreateContext("Bearer good-token", OwnBattleTag);

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
        var (context, invoked) = CreateContext("Bearer good-token", routeBattleTag);

        await filter.OnActionExecutionAsync(context, NextDelegate(invoked, context));

        Assert.That(invoked[0], Is.False);
        AssertUnauthorized(context);
    }

    [TestCase(null)]
    [TestCase("Basic aGk6dGhlcmU=")]
    public async Task MissingOrNonBearerAuthorizationHeader_Returns401_NotA500(string authorizationHeader)
    {
        var auth = new Mock<IW3CAuthenticationService>(MockBehavior.Strict);
        var filter = new BearerCheckIfBattleTagBelongsToAuthFilter(auth.Object);
        var (context, invoked) = CreateContext(authorizationHeader, OwnBattleTag);

        await filter.OnActionExecutionAsync(context, NextDelegate(invoked, context));

        Assert.That(invoked[0], Is.False);
        AssertUnauthorized(context);
        auth.Verify(a => a.GetUserByToken(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Test]
    public async Task ThrowingAuthService_Returns401_NextNotInvoked()
    {
        var auth = new Mock<IW3CAuthenticationService>();
        auth.Setup(a => a.GetUserByToken(It.IsAny<string>(), It.IsAny<bool>()))
            .Throws(new SecurityTokenValidationException("bad signature"));
        var filter = new BearerCheckIfBattleTagBelongsToAuthFilter(auth.Object);
        var (context, invoked) = CreateContext("Bearer garbage", OwnBattleTag);

        await filter.OnActionExecutionAsync(context, NextDelegate(invoked, context));

        Assert.That(invoked[0], Is.False);
        AssertUnauthorized(context);
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

    private static void AssertUnauthorized(ActionExecutingContext context)
    {
        Assert.That(context.Result, Is.TypeOf<UnauthorizedObjectResult>());
        var value = ((UnauthorizedObjectResult)context.Result).Value;
        Assert.That(value, Is.TypeOf<ErrorResult>());
        Assert.That(((ErrorResult)value).Error, Is.EqualTo("Sorry H4ckerb0i"));
    }

    private static (ActionExecutingContext context, bool[] invoked) CreateContext(string authorizationHeader, string routeBattleTag)
    {
        var httpContext = new DefaultHttpContext();
        if (authorizationHeader != null)
        {
            httpContext.Request.Headers[HeaderNames.Authorization] = authorizationHeader;
        }

        var routeData = new RouteData();
        if (routeBattleTag != null)
        {
            routeData.Values["battleTag"] = routeBattleTag;
        }

        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());
        var context = new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object>(),
            Mock.Of<Controller>());
        return (context, new bool[1]);
    }

    private static ActionExecutionDelegate NextDelegate(bool[] invoked, ActionExecutingContext context) =>
        () =>
        {
            invoked[0] = true;
            return Task.FromResult(new ActionExecutedContext(
                new ActionContext(context.HttpContext, new RouteData(), new ActionDescriptor()),
                new List<IFilterMetadata>(),
                Mock.Of<Controller>()));
        };
}
