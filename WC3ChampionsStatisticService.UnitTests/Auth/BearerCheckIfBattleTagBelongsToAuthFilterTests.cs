using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Moq;
using NUnit.Framework;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using W3ChampionsStatisticService.WebApi.ExceptionFilters;

namespace WC3ChampionsStatisticService.Tests.Auth;

[TestFixture]
public class BearerCheckIfBattleTagBelongsToAuthFilterTests
{
    private const string Token = "jwt";

    private static Mock<IW3CAuthenticationService> AuthReturning(string tokenBattleTag)
    {
        var auth = new Mock<IW3CAuthenticationService>();
        auth.Setup(a => a.GetUserByToken(Token, false))
            .Returns(new W3CUserAuthenticationDto { BattleTag = tokenBattleTag });
        return auth;
    }

    private static ActionExecutingContext CreateContext(string routeBattleTag, string authorization = "Bearer " + Token)
    {
        var httpContext = new DefaultHttpContext();
        if (authorization != null)
        {
            httpContext.Request.Headers["Authorization"] = authorization;
        }

        var routeData = new RouteData();
        if (routeBattleTag != null)
        {
            routeData.Values["battleTag"] = routeBattleTag;
        }

        return new ActionExecutingContext(
            new ActionContext(httpContext, routeData, new ActionDescriptor()),
            new List<IFilterMetadata>(),
            new Dictionary<string, object> { ["battleTag"] = routeBattleTag },
            Mock.Of<Controller>());
    }

    private sealed class NextRecorder(ActionExecutingContext context)
    {
        public bool WasInvoked { get; private set; }

        public ActionExecutionDelegate Delegate => () =>
        {
            WasInvoked = true;
            return Task.FromResult(new ActionExecutedContext(
                new ActionContext(context.HttpContext, new RouteData(), new ActionDescriptor()),
                new List<IFilterMetadata>(),
                Mock.Of<Controller>()));
        };
    }

    [Test]
    public async Task MatchingBattleTag_InvokesNextAndRewritesArgument()
    {
        var filter = new BearerCheckIfBattleTagBelongsToAuthFilter(AuthReturning("Alice#123").Object);
        var context = CreateContext("Alice#123");
        var next = new NextRecorder(context);

        await filter.OnActionExecutionAsync(context, next.Delegate);

        next.WasInvoked.Should().BeTrue();
        context.ActionArguments["battleTag"].Should().Be("Alice#123");
        context.Result.Should().BeNull();
    }

    [Test]
    public async Task DifferentBattleTag_Returns403AndSkipsAction()
    {
        var filter = new BearerCheckIfBattleTagBelongsToAuthFilter(AuthReturning("Alice#123").Object);
        var context = CreateContext("Bob#456");
        var next = new NextRecorder(context);

        await filter.OnActionExecutionAsync(context, next.Delegate);

        next.WasInvoked.Should().BeFalse();
        var result = context.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(403);
        result.Value.Should().BeOfType<ErrorResult>();
    }

    [Test]
    public async Task CaseDifferentBattleTag_Returns403()
    {
        var filter = new BearerCheckIfBattleTagBelongsToAuthFilter(AuthReturning("Alice#123").Object);
        var context = CreateContext("alice#123");
        var next = new NextRecorder(context);

        await filter.OnActionExecutionAsync(context, next.Delegate);

        next.WasInvoked.Should().BeFalse();
        context.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
    }

    [Test]
    public async Task MissingRouteBattleTag_Returns403()
    {
        var filter = new BearerCheckIfBattleTagBelongsToAuthFilter(AuthReturning("Alice#123").Object);
        var context = CreateContext(null);
        var next = new NextRecorder(context);

        await filter.OnActionExecutionAsync(context, next.Delegate);

        next.WasInvoked.Should().BeFalse();
        context.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
    }

    [Test]
    public async Task InvalidToken_Returns401()
    {
        var auth = new Mock<IW3CAuthenticationService>();
        auth.Setup(a => a.GetUserByToken(Token, false)).Throws<InvalidOperationException>();
        var filter = new BearerCheckIfBattleTagBelongsToAuthFilter(auth.Object);
        var context = CreateContext("Alice#123");
        var next = new NextRecorder(context);

        await filter.OnActionExecutionAsync(context, next.Delegate);

        next.WasInvoked.Should().BeFalse();
        context.Result.Should().BeOfType<UnauthorizedObjectResult>();
        auth.Verify(a => a.GetUserByToken(Token, false), Times.Once);
    }

    [TestCase(null)]
    [TestCase("Basic xyz")]
    public async Task MissingOrNonBearerAuthorizationHeader_Returns401WithoutTokenLookup(string authorization)
    {
        var auth = new Mock<IW3CAuthenticationService>();
        var filter = new BearerCheckIfBattleTagBelongsToAuthFilter(auth.Object);
        var context = CreateContext("Alice#123", authorization);
        var next = new NextRecorder(context);

        await filter.OnActionExecutionAsync(context, next.Delegate);

        next.WasInvoked.Should().BeFalse();
        context.Result.Should().BeOfType<UnauthorizedObjectResult>();
        auth.Verify(a => a.GetUserByToken(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Test]
    public async Task BearerWithoutToken_Returns401()
    {
        var auth = new Mock<IW3CAuthenticationService>();
        auth.Setup(a => a.GetUserByToken(It.IsAny<string>(), It.IsAny<bool>())).Throws<ArgumentNullException>();
        var filter = new BearerCheckIfBattleTagBelongsToAuthFilter(auth.Object);
        var context = CreateContext("Alice#123", "Bearer");
        var next = new NextRecorder(context);

        await filter.OnActionExecutionAsync(context, next.Delegate);

        next.WasInvoked.Should().BeFalse();
        context.Result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Test]
    public async Task ExceptionInsideAction_IsNotConvertedTo401()
    {
        var filter = new BearerCheckIfBattleTagBelongsToAuthFilter(AuthReturning("Alice#123").Object);
        var context = CreateContext("Alice#123");

        Func<Task> act = () => filter.OnActionExecutionAsync(context, () => throw new InvalidOperationException("boom"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        context.Result.Should().BeNull();
    }
}
