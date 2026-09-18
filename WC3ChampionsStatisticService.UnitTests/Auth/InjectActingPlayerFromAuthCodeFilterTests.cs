using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using W3C.Contracts.Admin.Permission;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using W3ChampionsStatisticService.WebApi.ExceptionFilters;
using static WC3ChampionsStatisticService.Tests.AuthFilterTestHelper;

namespace WC3ChampionsStatisticService.Tests.Auth;

[TestFixture]
public class InjectActingPlayerFromAuthCodeFilterTests
{
    private const string ExceptionDetail = "IDX10511: internal validation detail";

    [Test]
    public async Task ValidToken_InjectsTheActingPlayer_AndThePipelineContinues()
    {
        var user = new W3CUserAuthenticationDto { BattleTag = "peter#123", Name = "peter", Permissions = new HashSet<EPermission>() };
        var auth = new Mock<IW3CAuthenticationService>();
        auth.Setup(a => a.GetUserByToken("good-token", false)).Returns(user);
        var (context, invoked) = CreateActionExecutingContext("Bearer good-token");

        await new InjectActingPlayerFromAuthCodeFilter(auth.Object).OnActionExecutionAsync(context, NextDelegate(invoked, context));

        Assert.That(invoked[0], Is.True);
        Assert.That(context.Result, Is.Null);
        Assert.That(context.HttpContext.Items[InjectActingPlayerFromAuthCodeFilter.ActingPlayerUserKey], Is.SameAs(user));
    }

    [Test]
    public async Task ServiceReturningNoUser_KeepsItsDedicatedMessage()
    {
        var (context, invoked) = CreateActionExecutingContext("Bearer whatever");

        await new InjectActingPlayerFromAuthCodeFilter(Mock.Of<IW3CAuthenticationService>())
            .OnActionExecutionAsync(context, NextDelegate(invoked, context));

        Assert.That(invoked[0], Is.False);
        AssertUnauthorizedWithError(context.Result, "Unable to retrieve user from token");
    }

    [TestCaseSource(nameof(FailingAuthServices))]
    public async Task TokenVerificationThrows_Returns401_WithAFixedBody_AndNoExceptionText(IW3CAuthenticationService authService, string token)
    {
        var (context, invoked) = CreateActionExecutingContext("Bearer " + token);

        await new InjectActingPlayerFromAuthCodeFilter(authService).OnActionExecutionAsync(context, NextDelegate(invoked, context));

        Assert.That(invoked[0], Is.False);
        AssertUnauthorizedWithError(context.Result, "Unauthorized");
        Assert.That(((ErrorResult)((UnauthorizedObjectResult)context.Result).Value).Error, Does.Not.Contain(ExceptionDetail));
        Assert.That(context.HttpContext.Items.ContainsKey(InjectActingPlayerFromAuthCodeFilter.ActingPlayerUserKey), Is.False);
    }

    private static IEnumerable<TestCaseData> FailingAuthServices()
    {
        // The real service: IdentityModel diagnostics (IDX texts) must not reach the response body.
        yield return new TestCaseData(new W3CAuthenticationService(), "garbage")
            .SetName("{m}(real service, not a JWT: SecurityTokenMalformedException)");
        yield return new TestCaseData(new W3CAuthenticationService(), "a.b.c")
            .SetName("{m}(real service, undecodable segments: ArgumentException)");

        var throwing = new Mock<IW3CAuthenticationService>();
        throwing.Setup(a => a.GetUserByToken(It.IsAny<string>(), false)).Throws(new InvalidOperationException(ExceptionDetail));
        yield return new TestCaseData(throwing.Object, "any-token")
            .SetName("{m}(any other exception: its message is not echoed)");
    }
}
