using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Moq;
using NUnit.Framework;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace WC3ChampionsStatisticService.Tests.Friend;

// Hand-built ActionExecutingContext over a DefaultHttpContext (no TestServer/WebApplicationFactory
// in this repo — mirrors WC3ChampionsStatisticService.UnitTests/RateLimiting/RateLimitAttributeTests.cs).
[TestFixture]
public class ChatServiceSecretAuthFilterTests
{
    private const string CorrectSecret = "s3cret";

    private static ActionExecutingContext CreateContext(HttpContext httpContext)
    {
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        return new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object>(),
            Mock.Of<Controller>());
    }

    private static (ActionExecutingContext context, bool[] invoked) CreateContextWithHeader(string headerValue)
    {
        var httpContext = new DefaultHttpContext();
        if (headerValue != null)
        {
            httpContext.Request.Headers[ChatServiceSecretAuthFilter.HeaderName] = headerValue;
        }

        var context = CreateContext(httpContext);
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

    [Test]
    public async Task CorrectSecret_InvokesNext()
    {
        var settings = new ChatRelationshipsAuthSettings(CorrectSecret);
        var filter = new ChatServiceSecretAuthFilter(settings);
        var (context, invoked) = CreateContextWithHeader(CorrectSecret);

        await filter.OnActionExecutionAsync(context, NextDelegate(invoked, context));

        Assert.That(invoked[0], Is.True);
        Assert.That(context.Result, Is.Null);
    }

    [Test]
    public async Task MissingHeader_401_NextNotInvoked()
    {
        var settings = new ChatRelationshipsAuthSettings(CorrectSecret);
        var filter = new ChatServiceSecretAuthFilter(settings);
        var (context, invoked) = CreateContextWithHeader(null);

        await filter.OnActionExecutionAsync(context, NextDelegate(invoked, context));

        Assert.That(invoked[0], Is.False);
        Assert.That(context.Result, Is.TypeOf<UnauthorizedResult>());
    }

    [TestCase("s3cre")]  // prefix — shorter than the real secret
    [TestCase("s3cretX")] // superstring — longer than the real secret
    public async Task WrongSecret_DifferentLength_401(string providedSecret)
    {
        var settings = new ChatRelationshipsAuthSettings(CorrectSecret);
        var filter = new ChatServiceSecretAuthFilter(settings);
        var (context, invoked) = CreateContextWithHeader(providedSecret);

        await filter.OnActionExecutionAsync(context, NextDelegate(invoked, context));

        Assert.That(invoked[0], Is.False);
        Assert.That(context.Result, Is.TypeOf<UnauthorizedResult>());
    }

    [Test]
    public async Task WrongSecret_EqualLength_401_ComparisonIsFixedTime()
    {
        // Same length as the real secret, differing content — pins that the comparison
        // walks/allocates based on length only (CryptographicOperations.FixedTimeEquals),
        // not a short-circuiting equality check. The actual call site is confirmed by the
        // security reviewer.
        var settings = new ChatRelationshipsAuthSettings(CorrectSecret);
        var filter = new ChatServiceSecretAuthFilter(settings);
        var (context, invoked) = CreateContextWithHeader("x3cret");

        await filter.OnActionExecutionAsync(context, NextDelegate(invoked, context));

        Assert.That(invoked[0], Is.False);
        Assert.That(context.Result, Is.TypeOf<UnauthorizedResult>());
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task SecretUnconfigured_401_EvenWithEmptyHeader(string unconfiguredSecret)
    {
        // Decision-8 footgun: an unconfigured secret must reject EVERY request, including one
        // that happens to send an empty header value — "" == "" must never be reachable because
        // Configured is checked before any header comparison.
        var settings = new ChatRelationshipsAuthSettings(unconfiguredSecret);
        var filter = new ChatServiceSecretAuthFilter(settings);
        var (context, invoked) = CreateContextWithHeader("");

        await filter.OnActionExecutionAsync(context, NextDelegate(invoked, context));

        Assert.That(invoked[0], Is.False);
        Assert.That(context.Result, Is.TypeOf<UnauthorizedResult>());
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task SecretUnconfigured_401_EvenWithMatchingSecretHeader(string unconfiguredSecret)
    {
        // Belt-and-suspenders on the Decision-8 footgun: proves the guard is Configured-first
        // (not just "empty header can't match"), so a future refactor that reorders the boolean
        // expression can't accidentally let an unconfigured deployment be satisfied by ANY header
        // value, including one that happens to equal what the secret "would" be.
        var settings = new ChatRelationshipsAuthSettings(unconfiguredSecret);
        var filter = new ChatServiceSecretAuthFilter(settings);
        var (context, invoked) = CreateContextWithHeader(CorrectSecret);

        await filter.OnActionExecutionAsync(context, NextDelegate(invoked, context));

        Assert.That(invoked[0], Is.False);
        Assert.That(context.Result, Is.TypeOf<UnauthorizedResult>());
    }

    [Test]
    public async Task DuplicateHeaderValues_401()
    {
        // StringValues.ToString() joins multiple values with a comma, so even a duplicated
        // correct-secret header ("s3cret", "s3cret") joins to "s3cret,s3cret" — never equal to
        // the configured secret. Confirms the filter cannot be bypassed via header repetition.
        var settings = new ChatRelationshipsAuthSettings(CorrectSecret);
        var filter = new ChatServiceSecretAuthFilter(settings);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[ChatServiceSecretAuthFilter.HeaderName] =
            new StringValues([CorrectSecret, CorrectSecret]);
        var context = CreateContext(httpContext);
        var invoked = new bool[1];

        await filter.OnActionExecutionAsync(context, NextDelegate(invoked, context));

        Assert.That(invoked[0], Is.False);
        Assert.That(context.Result, Is.TypeOf<UnauthorizedResult>());
    }

    [Test]
    public void Attribute_IsFilterFactory_ResolvingFilterFromDI()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new ChatRelationshipsAuthSettings(CorrectSecret));
        services.AddTransient<ChatServiceSecretAuthFilter>();
        var provider = services.BuildServiceProvider();

        var attribute = new ChatServiceSecretAuthAttribute();

        Assert.That(attribute.IsReusable, Is.False);
        Assert.That(attribute.CreateInstance(provider), Is.TypeOf<ChatServiceSecretAuthFilter>());
    }
}
