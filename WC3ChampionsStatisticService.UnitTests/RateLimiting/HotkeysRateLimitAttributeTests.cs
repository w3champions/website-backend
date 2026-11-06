using System.Collections.Generic;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using W3ChampionsStatisticService.RateLimiting.Services;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace WC3ChampionsStatisticService.Tests.RateLimiting;

[TestFixture]
public class HotkeysRateLimitAttributeTests
{
    private Mock<IRateLimitBucketService> _bucket;
    private ActionExecutingContext _context;
    private HotkeysRateLimitAttribute _attribute;

    [SetUp]
    public void Setup()
    {
        _bucket = new Mock<IRateLimitBucketService>();
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IRateLimitService>());
        services.AddSingleton(_bucket.Object);
        services.AddSingleton(Mock.Of<ILogger<RateLimitAttribute>>());

        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        _context = new ActionExecutingContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(),
            new Dictionary<string, object> { ["battleTag"] = "PeTeR#1234" },
            Mock.Of<Controller>());
        _attribute = new HotkeysRateLimitAttribute();
    }

    private static RateLimitLease Lease(bool acquired)
    {
        var lease = new Mock<RateLimitLease>();
        lease.Setup(l => l.IsAcquired).Returns(acquired);
        return lease.Object;
    }

    private static ActionExecutionDelegate Next(ActionExecutingContext context) =>
        () => Task.FromResult(new ActionExecutedContext(context, new List<IFilterMetadata>(), Mock.Of<Controller>()));

    [Test]
    public async Task PartitionKey_IsPerBattleTagLowercased()
    {
        _bucket.Setup(b => b.TryAcquireAsync("user:peter#1234:hotkeys", 30, 200)).ReturnsAsync(Lease(true));

        await _attribute.OnActionExecutionAsync(_context, Next(_context));

        _bucket.Verify(b => b.TryAcquireAsync("user:peter#1234:hotkeys", 30, 200), Times.Once);
        _context.Result.Should().BeNull();
    }

    [Test]
    public void Order_RunsAfterBearerFilter()
    {
        _attribute.Order.Should().BeGreaterThan(0);
        _attribute.Scope.Should().Be("hotkeys");
    }

    [Test]
    public async Task ExceededLease_Returns429()
    {
        _bucket.Setup(b => b.TryAcquireAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(Lease(false));

        await _attribute.OnActionExecutionAsync(_context, Next(_context));

        _context.Result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(429);
    }
}
