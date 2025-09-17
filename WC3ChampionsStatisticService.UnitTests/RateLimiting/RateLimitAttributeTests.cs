using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using W3ChampionsStatisticService.RateLimiting.Models;
using W3ChampionsStatisticService.RateLimiting.Services;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace WC3ChampionsStatisticService.Tests.RateLimiting;

[TestFixture]
public class RateLimitAttributeTests
{
    private Mock<IRateLimitService> _rateLimitServiceMock;
    private Mock<IRateLimitBucketService> _bucketServiceMock;
    private Mock<ILogger<RateLimitAttribute>> _loggerMock;
    private RateLimitAttribute _attribute;
    private ActionExecutingContext _context;
    private HttpContext _httpContext;
    private ServiceCollection _services;

    [SetUp]
    public void Setup()
    {
        _rateLimitServiceMock = new Mock<IRateLimitService>();
        _bucketServiceMock = new Mock<IRateLimitBucketService>();
        _loggerMock = new Mock<ILogger<RateLimitAttribute>>();

        _services = new ServiceCollection();
        _services.AddSingleton(_rateLimitServiceMock.Object);
        _services.AddSingleton(_bucketServiceMock.Object);
        _services.AddSingleton(_loggerMock.Object);

        var serviceProvider = _services.BuildServiceProvider();

        _httpContext = new DefaultHttpContext();
        _httpContext.RequestServices = serviceProvider;
        _httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.168.1.1");

        var actionContext = new ActionContext(
            _httpContext,
            new RouteData(),
            new ActionDescriptor());

        _context = new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object>(),
            Mock.Of<Controller>()
        );

        _attribute = new RateLimitAttribute();
    }

    [Test]
    public async Task OnActionExecutionAsync_WithAcquiredLease_AllowsRequest()
    {
        _attribute.Scope = "test-scope";
        _attribute.HourlyLimit = 100;
        _attribute.DailyLimit = 1000;

        var rateLimitContext = new RateLimitContext
        {
            PolicyName = "default",
            HourlyLimit = 100,
            DailyLimit = 1000,
            HasValidApiToken = false,
            PartitionKey = "ip:192.168.1.1:default"
        };

        _rateLimitServiceMock.Setup(s => s.DetermineRateLimitContext(
            _httpContext, "test-scope", "default", 100, 1000))
            .ReturnsAsync(rateLimitContext);

        var lease = CreateAcquiredLease();
        _bucketServiceMock.Setup(b => b.TryAcquireAsync("ip:192.168.1.1:default", 100, 1000))
            .ReturnsAsync(lease);

        var next = new ActionExecutionDelegate(() => Task.FromResult(CreateActionExecutedContext()));

        await _attribute.OnActionExecutionAsync(_context, next);

        // Verify request was allowed to continue
        Assert.That(_context.Result, Is.Null);
    }

    [Test]
    public async Task OnActionExecutionAsync_WithNotAcquiredLease_Returns429()
    {
        _attribute.Scope = "test-scope";
        _attribute.HourlyLimit = 100;
        _attribute.DailyLimit = 1000;

        var rateLimitContext = new RateLimitContext
        {
            PolicyName = "default",
            HourlyLimit = 100,
            DailyLimit = 1000,
            HasValidApiToken = false,
            PartitionKey = "ip:192.168.1.1:default"
        };

        _rateLimitServiceMock.Setup(s => s.DetermineRateLimitContext(
            _httpContext, "test-scope", "default", 100, 1000))
            .ReturnsAsync(rateLimitContext);

        var lease = CreateNotAcquiredLease();
        _bucketServiceMock.Setup(b => b.TryAcquireAsync("ip:192.168.1.1:default", 100, 1000))
            .ReturnsAsync(lease);

        var next = new ActionExecutionDelegate(() => Task.FromResult(CreateActionExecutedContext()));

        await _attribute.OnActionExecutionAsync(_context, next);

        // Verify 429 response
        var result = _context.Result as StatusCodeResult;
        Assert.That(result, Is.Not.Null);
        Assert.That(result.StatusCode, Is.EqualTo(429));
    }

    [Test]
    public async Task OnActionExecutionAsync_WithApiToken_UsesTokenLimits()
    {
        _attribute.Scope = "api-scope";
        _attribute.HourlyLimit = 50;
        _attribute.DailyLimit = 500;

        var apiToken = new ApiToken
        {
            Token = "test-api-token",
            Name = "Test Token",
            Scopes = new Dictionary<string, ApiTokenScope>
            {
                ["api-scope"] = new ApiTokenScope { HourlyLimit = 200, DailyLimit = 2000, IsEnabled = true }
            }
        };

        var rateLimitContext = new RateLimitContext
        {
            PolicyName = "default",
            HourlyLimit = 200,
            DailyLimit = 2000,
            HasValidApiToken = true,
            ApiToken = apiToken,
            PartitionKey = "token:test-api-token:api-scope"
        };

        _httpContext.Request.Headers["X-API-Token"] = "test-api-token";

        _rateLimitServiceMock.Setup(s => s.DetermineRateLimitContext(
            _httpContext, "api-scope", "default", 50, 500))
            .ReturnsAsync(rateLimitContext);

        var lease = CreateAcquiredLease();
        _bucketServiceMock.Setup(b => b.TryAcquireAsync("token:test-api-token:api-scope", 200, 2000))
            .ReturnsAsync(lease);

        var next = new ActionExecutionDelegate(() => Task.FromResult(CreateActionExecutedContext()));

        await _attribute.OnActionExecutionAsync(_context, next);

        // Verify token limits were used
        _bucketServiceMock.Verify(b => b.TryAcquireAsync("token:test-api-token:api-scope", 200, 2000), Times.Once);
    }

    [Test]
    public async Task OnActionExecutionAsync_StoresRateLimitContextInHttpContext()
    {
        var rateLimitContext = new RateLimitContext
        {
            PolicyName = "test-policy",
            HourlyLimit = 100,
            DailyLimit = 1000,
            HasValidApiToken = false,
            PartitionKey = "ip:192.168.1.1:test-policy"
        };

        _rateLimitServiceMock.Setup(s => s.DetermineRateLimitContext(
            _httpContext, "default", "default", 100, 1000))
            .ReturnsAsync(rateLimitContext);

        var lease = CreateAcquiredLease();
        _bucketServiceMock.Setup(b => b.TryAcquireAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(lease);

        var next = new ActionExecutionDelegate(() => Task.FromResult(CreateActionExecutedContext()));

        await _attribute.OnActionExecutionAsync(_context, next);

        // Verify context was stored
        Assert.That(_httpContext.Items.ContainsKey("RateLimitContext"), Is.True);
        Assert.That(_httpContext.Items["RateLimitContext"], Is.EqualTo(rateLimitContext));
    }

    [Test]
    public async Task OnActionExecutionAsync_ExecutesNextDelegate()
    {
        var rateLimitContext = new RateLimitContext
        {
            PolicyName = "default",
            HourlyLimit = 100,
            DailyLimit = 1000,
            HasValidApiToken = false,
            PartitionKey = "ip:192.168.1.1:default"
        };

        _rateLimitServiceMock.Setup(s => s.DetermineRateLimitContext(
            It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(rateLimitContext);

        var lease = CreateAcquiredLease();

        _bucketServiceMock.Setup(b => b.TryAcquireAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(lease);

        var nextExecuted = false;
        var next = new ActionExecutionDelegate(() =>
        {
            nextExecuted = true;
            return Task.FromResult(CreateActionExecutedContext());
        });

        await _attribute.OnActionExecutionAsync(_context, next);

        // Verify the next delegate was executed
        Assert.That(nextExecuted, Is.True);
    }

    [Test]
    public async Task OnActionExecutionAsync_WithException_PropagatesException()
    {
        var rateLimitContext = new RateLimitContext
        {
            PolicyName = "default",
            HourlyLimit = 100,
            DailyLimit = 1000,
            HasValidApiToken = false,
            PartitionKey = "ip:192.168.1.1:default"
        };

        _rateLimitServiceMock.Setup(s => s.DetermineRateLimitContext(
            It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(rateLimitContext);

        var lease = CreateAcquiredLease();

        _bucketServiceMock.Setup(b => b.TryAcquireAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(lease);

        var next = new ActionExecutionDelegate(() => throw new InvalidOperationException("Test exception"));

        // Verify exception is propagated
        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _attribute.OnActionExecutionAsync(_context, next));

        Assert.That(ex.Message, Is.EqualTo("Test exception"));
    }



    private RateLimitLease CreateAcquiredLease()
    {
        var lease = new Mock<RateLimitLease>();
        lease.Setup(l => l.IsAcquired).Returns(true);
        return lease.Object;
    }

    private RateLimitLease CreateNotAcquiredLease()
    {
        var lease = new Mock<RateLimitLease>();
        lease.Setup(l => l.IsAcquired).Returns(false);
        return lease.Object;
    }

    private ActionExecutedContext CreateActionExecutedContext()
    {
        return new ActionExecutedContext(
            new ActionContext(_httpContext, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(),
            Mock.Of<Controller>()
        );
    }
}