using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Net.Http.Headers;
using Moq;
using NUnit.Framework;
using W3ChampionsStatisticService.WebApi.ExceptionFilters;

namespace WC3ChampionsStatisticService.Tests;

/// <summary>
/// Hand-built filter contexts over a <see cref="DefaultHttpContext"/> for the auth filter fixtures. The repo has
/// no TestServer/WebApplicationFactory, so filters are driven directly (as in
/// <c>Friend/ChatServiceSecretAuthFilterTests.cs</c>).
/// </summary>
public static class AuthFilterTestHelper
{
    /// <summary>The generic 401 body of the legacy bearer filters.</summary>
    public const string LegacyUnauthorizedError = "Sorry H4ckerb0i";

    public static DefaultHttpContext CreateHttpContext(string authorizationHeader)
    {
        var httpContext = new DefaultHttpContext();
        if (authorizationHeader != null)
        {
            httpContext.Request.Headers[HeaderNames.Authorization] = authorizationHeader;
        }

        return httpContext;
    }

    /// <returns>The context, plus a one-element flag array that <see cref="NextDelegate"/> sets when the pipeline continues.</returns>
    public static (ActionExecutingContext context, bool[] nextInvoked) CreateActionExecutingContext(
        string authorizationHeader, string routeBattleTag = null)
    {
        var routeData = new RouteData();
        if (routeBattleTag != null)
        {
            routeData.Values["battleTag"] = routeBattleTag;
        }

        var actionContext = new ActionContext(CreateHttpContext(authorizationHeader), routeData, new ActionDescriptor());
        var context = new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object>(),
            Mock.Of<Controller>());
        return (context, new bool[1]);
    }

    public static ActionExecutionDelegate NextDelegate(bool[] nextInvoked, ActionExecutingContext context) =>
        () =>
        {
            nextInvoked[0] = true;
            return Task.FromResult(new ActionExecutedContext(
                new ActionContext(context.HttpContext, new RouteData(), new ActionDescriptor()),
                new List<IFilterMetadata>(),
                Mock.Of<Controller>()));
        };

    /// <summary>Asserts the exact 401 result: an <see cref="UnauthorizedObjectResult"/> whose body is <c>{ error = expectedError }</c>.</summary>
    public static void AssertUnauthorizedWithError(IActionResult result, string expectedError)
    {
        Assert.That(result, Is.TypeOf<UnauthorizedObjectResult>());
        var value = ((UnauthorizedObjectResult)result).Value;
        Assert.That(value, Is.TypeOf<ErrorResult>());
        Assert.That(((ErrorResult)value).Error, Is.EqualTo(expectedError));
    }
}
