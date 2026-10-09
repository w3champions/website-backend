using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Moq;
using NUnit.Framework;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace WC3ChampionsStatisticService.Tests.Friend;

[TestFixture]
public class InjectAuthTokenFilterTests
{
    private static (ActionExecutingContext context, ActionExecutionDelegate next, bool[] invoked) Arrange(string authorization)
    {
        var httpContext = new DefaultHttpContext();
        if (authorization != null)
        {
            httpContext.Request.Headers["Authorization"] = authorization;
        }

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var context = new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object>(),
            Mock.Of<Controller>());

        var invoked = new bool[1];
        ActionExecutionDelegate next = () =>
        {
            invoked[0] = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), Mock.Of<Controller>()));
        };
        return (context, next, invoked);
    }

    [Test]
    public async Task BearerToken_InjectsTokenInvokesNextAndLeavesResultUnset()
    {
        var (context, next, invoked) = Arrange("Bearer x");

        await new InjectAuthTokenFilter().OnActionExecutionAsync(context, next);

        Assert.IsTrue(invoked[0]);
        Assert.AreEqual("x", context.ActionArguments["authToken"]);
        Assert.IsNull(context.Result);
    }

    [TestCase(null)]
    [TestCase("Basic abc")]
    public async Task MissingOrNonBearerToken_Returns401WithoutInvokingNext(string authorization)
    {
        var (context, next, invoked) = Arrange(authorization);

        await new InjectAuthTokenFilter().OnActionExecutionAsync(context, next);

        Assert.IsFalse(invoked[0]);
        Assert.IsInstanceOf<UnauthorizedObjectResult>(context.Result);
        Assert.IsFalse(context.ActionArguments.ContainsKey("authToken"));
    }
}
