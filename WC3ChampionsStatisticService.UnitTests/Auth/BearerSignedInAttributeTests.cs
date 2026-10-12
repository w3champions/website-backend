using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Moq;
using NUnit.Framework;
using W3C.Contracts.Admin.Permission;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace WC3ChampionsStatisticService.Tests.Auth;

// Hand-built ActionExecutingContext over a DefaultHttpContext, as in ChatServiceSecretAuthFilterTests.
[TestFixture]
public class BearerSignedInAttributeTests
{
    private static (ActionExecutingContext context, bool[] invoked) CreateContext(string authorization, Mock<IW3CAuthenticationService> auth)
    {
        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(auth.Object).BuildServiceProvider(),
        };
        if (authorization != null)
        {
            httpContext.Request.Headers.Authorization = authorization;
        }

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var context = new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), new Dictionary<string, object>(), Mock.Of<Controller>());
        return (context, new bool[1]);
    }

    private static ActionExecutionDelegate Next(bool[] invoked, ActionExecutingContext context) =>
        () =>
        {
            invoked[0] = true;
            return Task.FromResult(new ActionExecutedContext(context, new List<IFilterMetadata>(), Mock.Of<Controller>()));
        };

    private static Mock<IW3CAuthenticationService> AuthReturning(string battleTag, bool isAdmin = false, params EPermission[] permissions)
    {
        var auth = new Mock<IW3CAuthenticationService>();
        auth.Setup(a => a.GetUserByToken("token", true)).Returns(new W3CUserAuthenticationDto
        {
            BattleTag = battleTag,
            IsAdmin = isAdmin,
            Permissions = [.. permissions],
        });
        return auth;
    }

    private static async Task<(ActionExecutingContext context, bool invoked)> Run(BearerSignedInAttribute filter, Mock<IW3CAuthenticationService> auth, string authorization = "Bearer token")
    {
        var (context, invoked) = CreateContext(authorization, auth);
        await filter.OnActionExecutionAsync(context, Next(invoked, context));
        return (context, invoked[0]);
    }

    [Test]
    public async Task SignedInUser_PassesWithTheBattleTagOfTheToken()
    {
        var (context, invoked) = await Run(new BearerSignedInAttribute(), AuthReturning("Buyer#1"));

        Assert.That(invoked, Is.True);
        Assert.That(context.ActionArguments["battleTag"], Is.EqualTo("Buyer#1"));
    }

    [TestCase(null)]
    [TestCase("token")]
    [TestCase("Basic token")]
    public async Task NoBearerToken_Is401(string authorization)
    {
        var (context, invoked) = await Run(new BearerSignedInAttribute(), AuthReturning("Buyer#1"), authorization);

        Assert.That(invoked, Is.False);
        Assert.That(context.Result, Is.TypeOf<UnauthorizedObjectResult>());
    }

    [Test]
    public async Task InvalidToken_Is401()
    {
        var auth = new Mock<IW3CAuthenticationService>();
        auth.Setup(a => a.GetUserByToken(It.IsAny<string>(), true)).Throws(new SecurityTokenInvalidSignatureException());

        var (context, invoked) = await Run(new BearerSignedInAttribute(), auth);

        Assert.That(invoked, Is.False);
        Assert.That(context.Result, Is.TypeOf<UnauthorizedObjectResult>());
    }

    [Test]
    public async Task ExpiredToken_Is401WithTheExpiredCode()
    {
        var auth = new Mock<IW3CAuthenticationService>();
        auth.Setup(a => a.GetUserByToken(It.IsAny<string>(), true)).Throws(new SecurityTokenExpiredException());

        var (context, invoked) = await Run(new BearerSignedInAttribute(), auth);

        Assert.That(invoked, Is.False);
        var result = (UnauthorizedObjectResult)context.Result;
        Assert.That(result.Value.GetType().GetProperty("Error")!.GetValue(result.Value), Is.EqualTo("AUTH_TOKEN_EXPIRED"));
    }

    [Test]
    public async Task OnlyWithPermission_LetsAdminsWithThePermissionPass()
    {
        var filter = new BearerSignedInAttribute { OnlyWithPermission = EPermission.Replays };

        var (context, invoked) = await Run(filter, AuthReturning("Admin#1", true, EPermission.Moderation, EPermission.Replays));

        Assert.That(invoked, Is.True);
        Assert.That(context.ActionArguments["battleTag"], Is.EqualTo("Admin#1"));
    }

    [Test]
    public async Task OnlyWithPermission_RefusesOtherUsersWith403()
    {
        var filter = new BearerSignedInAttribute { OnlyWithPermission = EPermission.Replays };
        var users = new[]
        {
            AuthReturning("Buyer#1"),
            AuthReturning("Admin#1", true, EPermission.Moderation),
            // The permission without the admin flag is not enough, as in BearerHasPermissionFilter.
            AuthReturning("Buyer#2", false, EPermission.Replays),
        };

        foreach (var user in users)
        {
            var (context, invoked) = await Run(filter, user);

            Assert.That(invoked, Is.False);
            Assert.That(context.Result, Is.TypeOf<ObjectResult>());
            Assert.That(((ObjectResult)context.Result).StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
            Assert.That(context.ActionArguments.ContainsKey("battleTag"), Is.False);
        }
    }

    [Test]
    public async Task OnlyWithPermission_ZeroIsARealPermission()
    {
        // EPermission.Permissions is 0, the default value of the enum: it must still be checked.
        var filter = new BearerSignedInAttribute { OnlyWithPermission = EPermission.Permissions };

        var (_, refused) = await Run(filter, AuthReturning("Admin#1", true, EPermission.Replays));
        var (_, passed) = await Run(filter, AuthReturning("Admin#2", true, EPermission.Permissions));

        Assert.That(refused, Is.False);
        Assert.That(passed, Is.True);
    }
}
