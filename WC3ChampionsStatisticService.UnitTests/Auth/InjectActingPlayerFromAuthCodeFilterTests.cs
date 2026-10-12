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
using W3ChampionsStatisticService.WebApi.ExceptionFilters;

namespace WC3ChampionsStatisticService.Tests.Auth;

[TestFixture]
public class InjectActingPlayerFromAuthCodeFilterTests
{
    private static (ActionExecutingContext context, ActionExecutionDelegate next, bool[] invoked) Arrange(string authorization)
    {
        var httpContext = new DefaultHttpContext();
        if (authorization != null)
        {
            httpContext.Request.Headers["Authorization"] = authorization;
        }
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var context = new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), new Dictionary<string, object>(), Mock.Of<Controller>());
        var invoked = new bool[1];
        ActionExecutionDelegate next = () =>
        {
            invoked[0] = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), Mock.Of<Controller>()));
        };
        return (context, next, invoked);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("Basic abc")]
    public async Task MissingOrNonBearerHeader_Returns401_AndDoesNotRunAction(string authorization)
    {
        var (context, next, invoked) = Arrange(authorization);
        var filter = new InjectActingPlayerFromAuthCodeFilter(Mock.Of<IW3CAuthenticationService>());

        await filter.OnActionExecutionAsync(context, next);

        Assert.IsFalse(invoked[0]);
        var result = context.Result as UnauthorizedObjectResult;
        Assert.IsNotNull(result, "a missing token must produce 401, not an empty 200");
        Assert.AreEqual("Missing bearer token", ((ErrorResult)result.Value).Error);
    }

    [Test]
    public async Task ValidBearer_InjectsActingPlayer_AndRunsAction()
    {
        var (context, next, invoked) = Arrange("Bearer jwt");
        var auth = new Mock<IW3CAuthenticationService>();
        auth.Setup(a => a.GetUserByToken("jwt", false)).Returns(new W3CUserAuthenticationDto { BattleTag = "Player#1234" });

        await new InjectActingPlayerFromAuthCodeFilter(auth.Object).OnActionExecutionAsync(context, next);

        Assert.IsTrue(invoked[0]);
        Assert.IsNull(context.Result);
        Assert.AreEqual("Player#1234", InjectActingPlayerAuthCodeAttribute.GetActingPlayerUser(context.HttpContext).BattleTag);
    }
}
