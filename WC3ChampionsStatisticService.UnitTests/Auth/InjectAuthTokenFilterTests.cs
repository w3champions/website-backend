using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using static WC3ChampionsStatisticService.Tests.AuthFilterTestHelper;

namespace WC3ChampionsStatisticService.Tests.Auth;

[TestFixture]
public class InjectAuthTokenFilterTests
{
    [Test]
    public async Task BearerToken_IsInjected_AndThePipelineContinuesWithoutAResult()
    {
        var filter = new InjectAuthTokenFilter();
        var (context, invoked) = CreateActionExecutingContext("Bearer some-token");

        await filter.OnActionExecutionAsync(context, NextDelegate(invoked, context));

        Assert.That(invoked[0], Is.True);
        Assert.That(context.ActionArguments["authToken"], Is.EqualTo("some-token"));
        Assert.That(context.Result, Is.Null, "the 401 belongs to the deny path only, never after the action ran");
    }

    [TestCase(null)]
    [TestCase("Basic aGk6dGhlcmU=")]
    [TestCase("Bearer")]
    public async Task MissingOrNonBearerToken_Returns401_NextNotInvoked(string authorizationHeader)
    {
        var filter = new InjectAuthTokenFilter();
        var (context, invoked) = CreateActionExecutingContext(authorizationHeader);

        await filter.OnActionExecutionAsync(context, NextDelegate(invoked, context));

        Assert.That(invoked[0], Is.False);
        AssertUnauthorizedWithError(context.Result, LegacyUnauthorizedError);
    }
}
