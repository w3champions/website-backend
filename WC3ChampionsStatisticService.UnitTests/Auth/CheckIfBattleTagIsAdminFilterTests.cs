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
public class CheckIfBattleTagIsAdminFilterTests
{
    [Test]
    public async Task Admin_InvokesNext_AndInjectsBattleTag()
    {
        var filter = new CheckIfBattleTagIsAdminFilter(AuthReturning("admin#123", isAdmin: true).Object);
        var (context, invoked) = CreateContext("Bearer good-token");

        await filter.OnActionExecutionAsync(context, NextDelegate(invoked, context));

        Assert.That(invoked[0], Is.True);
        Assert.That(context.Result, Is.Null);
        Assert.That(context.ActionArguments["battleTag"], Is.EqualTo("admin#123"));
    }

    [Test]
    public async Task NonAdmin_Returns401_NextNotInvoked()
    {
        var filter = new CheckIfBattleTagIsAdminFilter(AuthReturning("peter#123", isAdmin: false).Object);
        var (context, invoked) = CreateContext("Bearer good-token");

        await filter.OnActionExecutionAsync(context, NextDelegate(invoked, context));

        Assert.That(invoked[0], Is.False);
        AssertUnauthorized(context);
    }

    [Test]
    public async Task AdminTokenWithoutBattleTag_Returns401_NextNotInvoked()
    {
        var filter = new CheckIfBattleTagIsAdminFilter(AuthReturning("", isAdmin: true).Object);
        var (context, invoked) = CreateContext("Bearer good-token");

        await filter.OnActionExecutionAsync(context, NextDelegate(invoked, context));

        Assert.That(invoked[0], Is.False);
        AssertUnauthorized(context);
    }

    [TestCase(null)]
    [TestCase("Basic aGk6dGhlcmU=")]
    public async Task MissingOrNonBearerAuthorizationHeader_Returns401(string authorizationHeader)
    {
        var auth = new Mock<IW3CAuthenticationService>(MockBehavior.Strict);
        var filter = new CheckIfBattleTagIsAdminFilter(auth.Object);
        var (context, invoked) = CreateContext(authorizationHeader);

        await filter.OnActionExecutionAsync(context, NextDelegate(invoked, context));

        Assert.That(invoked[0], Is.False);
        AssertUnauthorized(context);
        auth.Verify(a => a.GetUserByToken(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Test]
    public async Task ExpiredToken_KeepsItsDedicatedTokenExpiredResponse()
    {
        var auth = new Mock<IW3CAuthenticationService>();
        auth.Setup(a => a.GetUserByToken(It.IsAny<string>(), It.IsAny<bool>()))
            .Throws(new SecurityTokenExpiredException("expired"));
        var filter = new CheckIfBattleTagIsAdminFilter(auth.Object);
        var (context, invoked) = CreateContext("Bearer stale");

        await filter.OnActionExecutionAsync(context, NextDelegate(invoked, context));

        Assert.That(invoked[0], Is.False);
        Assert.That(context.Result, Is.TypeOf<UnauthorizedObjectResult>());
        Assert.That(((UnauthorizedObjectResult)context.Result).Value, Is.Not.TypeOf<ErrorResult>(),
            "an expired admin token keeps its AUTH_TOKEN_EXPIRED body so clients can prompt a re-login");
    }

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

    private static void AssertUnauthorized(ActionExecutingContext context)
    {
        Assert.That(context.Result, Is.TypeOf<UnauthorizedObjectResult>());
        var value = ((UnauthorizedObjectResult)context.Result).Value;
        Assert.That(value, Is.TypeOf<ErrorResult>());
        Assert.That(((ErrorResult)value).Error, Is.EqualTo("Sorry H4ckerb0i"));
    }

    private static (ActionExecutingContext context, bool[] invoked) CreateContext(string authorizationHeader)
    {
        var httpContext = new DefaultHttpContext();
        if (authorizationHeader != null)
        {
            httpContext.Request.Headers[HeaderNames.Authorization] = authorizationHeader;
        }

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
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
