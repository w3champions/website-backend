using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using W3ChampionsStatisticService.RateLimiting.Models;
using W3ChampionsStatisticService.RateLimiting.Services;

namespace WC3ChampionsStatisticService.Tests.RateLimiting;

[TestFixture]
public class RateLimitServiceTests
{
    private Mock<IApiTokenService> _tokenServiceMock;
    private Mock<ILogger<RateLimitService>> _loggerMock;
    private RateLimitService _service;
    private DefaultHttpContext _httpContext;

    [SetUp]
    public void Setup()
    {
        _tokenServiceMock = new Mock<IApiTokenService>();
        _loggerMock = new Mock<ILogger<RateLimitService>>();
        _service = new RateLimitService(_tokenServiceMock.Object, _loggerMock.Object);
        _httpContext = new DefaultHttpContext();
        _httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.168.1.1");
    }

    [Test]
    public async Task DetermineRateLimitContext_WithoutToken_UsesIPBasedLimits()
    {
        var scope = "test";
        var policyName = "default";
        var hourlyLimit = 100;
        var dailyLimit = 1000;

        var context = await _service.DetermineRateLimitContext(_httpContext, scope, policyName, hourlyLimit, dailyLimit);

        Assert.That(context.PolicyName, Is.EqualTo(policyName));
        Assert.That(context.HourlyLimit, Is.EqualTo(hourlyLimit));
        Assert.That(context.DailyLimit, Is.EqualTo(dailyLimit));
        Assert.That(context.HasValidApiToken, Is.False);
        Assert.That(context.PartitionKey, Is.EqualTo("ip:192.168.1.1:test"));
    }

    [Test]
    public async Task DetermineRateLimitContext_WithValidToken_UsesTokenLimits()
    {
        var token = "valid-token";
        var scope = "replay";
        var apiToken = new ApiToken
        {
            Token = token,
            Name = "Test Token",
            Scopes = new Dictionary<string, ApiTokenScope>
            {
                [scope] = new ApiTokenScope { HourlyLimit = 200, DailyLimit = 2000, IsEnabled = true }
            }
        };

        _httpContext.Request.Headers["X-API-Token"] = token;
        _tokenServiceMock.Setup(t => t.ValidateToken(token, "192.168.1.1", scope))
            .ReturnsAsync(apiToken);

        var context = await _service.DetermineRateLimitContext(_httpContext, scope, "test", 100, 1000);

        Assert.That(context.HasValidApiToken, Is.True);
        Assert.That(context.ApiToken, Is.EqualTo(apiToken));
        Assert.That(context.HourlyLimit, Is.EqualTo(200));
        Assert.That(context.DailyLimit, Is.EqualTo(2000));
        Assert.That(context.PartitionKey, Is.EqualTo($"token:{apiToken.Id}:{scope}"));
    }

    [Test]
    public async Task DetermineRateLimitContext_WithInvalidToken_FallsBackToIP()
    {
        var token = "invalid-token";
        var scope = "replay";
        var policyName = "default";

        _httpContext.Request.Headers["X-API-Token"] = token;
        _tokenServiceMock.Setup(t => t.ValidateToken(token, "192.168.1.1", scope))
            .ReturnsAsync((ApiToken)null);

        var context = await _service.DetermineRateLimitContext(_httpContext, scope, policyName, 100, 1000);

        Assert.That(context.HasValidApiToken, Is.False);
        Assert.That(context.ApiToken, Is.Null);
        Assert.That(context.HourlyLimit, Is.EqualTo(100));
        Assert.That(context.DailyLimit, Is.EqualTo(1000));
        Assert.That(context.PartitionKey, Is.EqualTo("ip:192.168.1.1:replay"));
    }

    [Test]
    public async Task DetermineRateLimitContext_TokenWithScope_UsesCorrectLimits()
    {
        var token = "token-with-scope";
        var scope = "stats";
        var apiToken = new ApiToken
        {
            Token = token,
            Name = "Test Token",
            Scopes = new Dictionary<string, ApiTokenScope>
            {
                [scope] = new ApiTokenScope { HourlyLimit = 150, DailyLimit = 1500, IsEnabled = true },
                ["extra"] = new ApiTokenScope { HourlyLimit = 10000, DailyLimit = 100000, IsEnabled = true }
            }
        };

        _httpContext.Request.Headers["X-API-Token"] = token;
        _tokenServiceMock.Setup(t => t.ValidateToken(token, "192.168.1.1", scope))
            .ReturnsAsync(apiToken);

        var context = await _service.DetermineRateLimitContext(_httpContext, scope, "test", 100, 1000);

        Assert.That(context.HasValidApiToken, Is.True);
        // Should use only the requested scope's limits, not combined
        Assert.That(context.HourlyLimit, Is.EqualTo(150));
        Assert.That(context.DailyLimit, Is.EqualTo(1500));
        Assert.That(context.PartitionKey, Is.EqualTo($"token:{apiToken.Id}:{scope}"));
    }

    [Test]
    public void GetIpAddress_WithXForwardedFor_UsesFirstIP()
    {
        _httpContext.Request.Headers["X-Forwarded-For"] = "10.0.0.1, 192.168.1.100";

        var ipAddress = _service.GetIpAddress(_httpContext);

        Assert.That(ipAddress, Is.EqualTo("10.0.0.1"));
    }

    [Test]
    public void GetIpAddress_WithCFConnectingIP_UsesCFIP()
    {
        _httpContext.Request.Headers["CF-Connecting-IP"] = "203.0.113.1";

        var ipAddress = _service.GetIpAddress(_httpContext);

        Assert.That(ipAddress, Is.EqualTo("203.0.113.1"));
    }

    [Test]
    public void GetIpAddress_WithoutHeaders_UsesRemoteIP()
    {
        var ipAddress = _service.GetIpAddress(_httpContext);

        Assert.That(ipAddress, Is.EqualTo("192.168.1.1"));
    }

    [Test]
    public void GetIpAddress_WithNullRemoteIP_ReturnsUnknown()
    {
        _httpContext.Connection.RemoteIpAddress = null;

        var ipAddress = _service.GetIpAddress(_httpContext);

        Assert.That(ipAddress, Is.EqualTo(string.Empty));
    }


    [Test]
    public async Task DetermineRateLimitContext_TokenWithoutRequestedScope_FallsBackToIP()
    {
        var token = "token-without-scope";
        var scope = "replay";
        var apiToken = new ApiToken
        {
            Token = token,
            Name = "Test Token",
            Scopes = new Dictionary<string, ApiTokenScope>
            {
                ["stats"] = new ApiTokenScope { HourlyLimit = 100, DailyLimit = 1000, IsEnabled = true }
            }
        };

        _httpContext.Request.Headers["X-API-Token"] = token;
        _tokenServiceMock.Setup(t => t.ValidateToken(token, "192.168.1.1", scope))
            .ReturnsAsync((ApiToken)null); // Token doesn't have the requested scope

        var context = await _service.DetermineRateLimitContext(_httpContext, scope, "default", 50, 500);

        Assert.That(context.HasValidApiToken, Is.False);
        Assert.That(context.HourlyLimit, Is.EqualTo(50));
        Assert.That(context.DailyLimit, Is.EqualTo(500));
        Assert.That(context.PartitionKey, Is.EqualTo("ip:192.168.1.1:replay"));
    }

    [Test]
    public void GetIpAddress_PrefersCFConnectingIPOverXForwardedFor()
    {
        _httpContext.Request.Headers["X-Forwarded-For"] = "10.0.0.1, 192.168.1.100";
        _httpContext.Request.Headers["CF-Connecting-IP"] = "203.0.113.1";

        var ipAddress = _service.GetIpAddress(_httpContext);

        // CF-Connecting-IP should take precedence
        Assert.That(ipAddress, Is.EqualTo("203.0.113.1"));
    }
}
