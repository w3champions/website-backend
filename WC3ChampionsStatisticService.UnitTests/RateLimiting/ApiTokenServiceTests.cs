using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using W3ChampionsStatisticService.RateLimiting.Models;
using W3ChampionsStatisticService.RateLimiting.Repositories;
using W3ChampionsStatisticService.RateLimiting.Services;

namespace WC3ChampionsStatisticService.Tests.RateLimiting;

[TestFixture]
public class ApiTokenServiceTests
{
    private Mock<IApiTokenRepository> _repositoryMock;
    private IMemoryCache _cache;
    private Mock<ILogger<ApiTokenService>> _loggerMock;
    private ApiTokenService _service;

    [SetUp]
    public void Setup()
    {
        _repositoryMock = new Mock<IApiTokenRepository>();
        _cache = new MemoryCache(new MemoryCacheOptions());
        _loggerMock = new Mock<ILogger<ApiTokenService>>();
        _service = new ApiTokenService(_repositoryMock.Object, _cache, _loggerMock.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _cache?.Dispose();
    }

    [Test]
    public async Task ValidateToken_WithValidActiveToken_ReturnsToken()
    {
        var token = new ApiToken
        {
            Id = "test-id",
            Name = "Test Token",
            Token = "valid-token",
            IsActive = true,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
        };
        _repositoryMock.Setup(r => r.GetByToken("valid-token"))
            .ReturnsAsync(token);

        var result = await _service.ValidateToken("valid-token", "192.168.1.1");

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Name, Is.EqualTo("Test Token"));
        _repositoryMock.Verify(r => r.UpdateLastUsed("valid-token"), Times.Once);
    }

    [Test]
    public async Task ValidateToken_WithInactiveToken_ReturnsNull()
    {
        var token = new ApiToken
        {
            Token = "inactive-token",
            IsActive = false
        };
        _repositoryMock.Setup(r => r.GetByToken("inactive-token"))
            .ReturnsAsync(token);

        var result = await _service.ValidateToken("inactive-token", "192.168.1.1");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ValidateToken_WithExpiredToken_ReturnsNull()
    {
        var token = new ApiToken
        {
            Token = "expired-token",
            IsActive = true,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
        _repositoryMock.Setup(r => r.GetByToken("expired-token"))
            .ReturnsAsync(token);

        var result = await _service.ValidateToken("expired-token", "192.168.1.1");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ValidateToken_WithIPRestriction_AllowedIP_ReturnsToken()
    {
        var token = new ApiToken
        {
            Token = "ip-restricted",
            IsActive = true,
            AllowedIPs = new[] { "192.168.1.1", "10.0.0.1" }
        };
        _repositoryMock.Setup(r => r.GetByToken("ip-restricted"))
            .ReturnsAsync(token);

        var result = await _service.ValidateToken("ip-restricted", "192.168.1.1");

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task ValidateToken_WithIPRestriction_NotAllowedIP_ReturnsNull()
    {
        var token = new ApiToken
        {
            Token = "ip-restricted",
            IsActive = true,
            AllowedIPs = new[] { "192.168.1.1", "10.0.0.1" }
        };
        _repositoryMock.Setup(r => r.GetByToken("ip-restricted"))
            .ReturnsAsync(token);

        var result = await _service.ValidateToken("ip-restricted", "192.168.2.1");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ValidateToken_WithRequiredScope_HasScope_ReturnsToken()
    {
        var token = new ApiToken
        {
            Token = "scoped-token",
            IsActive = true,
            Scopes = new Dictionary<string, ApiTokenScope>
            {
                ["replay"] = new ApiTokenScope { IsEnabled = true }
            }
        };
        _repositoryMock.Setup(r => r.GetByToken("scoped-token"))
            .ReturnsAsync(token);

        var result = await _service.ValidateToken("scoped-token", "192.168.1.1", "replay");

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task ValidateToken_WithRequiredScope_MissingScope_ReturnsNull()
    {
        var token = new ApiToken
        {
            Token = "scoped-token",
            IsActive = true,
            Scopes = new Dictionary<string, ApiTokenScope>
            {
                ["stats"] = new ApiTokenScope { IsEnabled = true }
            }
        };
        _repositoryMock.Setup(r => r.GetByToken("scoped-token"))
            .ReturnsAsync(token);

        var result = await _service.ValidateToken("scoped-token", "192.168.1.1", "replay");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ValidateToken_WithRequiredScope_DisabledScope_ReturnsNull()
    {
        var token = new ApiToken
        {
            Token = "scoped-token",
            IsActive = true,
            Scopes = new Dictionary<string, ApiTokenScope>
            {
                ["replay"] = new ApiTokenScope { IsEnabled = false }
            }
        };
        _repositoryMock.Setup(r => r.GetByToken("scoped-token"))
            .ReturnsAsync(token);

        var result = await _service.ValidateToken("scoped-token", "192.168.1.1", "replay");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ValidateToken_CachesValidToken()
    {
        var token = new ApiToken
        {
            Token = "cached-token",
            IsActive = true
        };
        _repositoryMock.Setup(r => r.GetByToken("cached-token"))
            .ReturnsAsync(token);

        // First call - loads from repository
        var result1 = await _service.ValidateToken("cached-token", null);
        Assert.That(result1, Is.Not.Null);

        // Second call - should use cache
        var result2 = await _service.ValidateToken("cached-token", null);
        Assert.That(result2, Is.Not.Null);

        // Repository should only be called once
        _repositoryMock.Verify(r => r.GetByToken("cached-token"), Times.Once);
    }

    [Test]
    public async Task ValidateToken_WithNullToken_ReturnsNull()
    {
        var result = await _service.ValidateToken(null, "192.168.1.1");

        Assert.That(result, Is.Null);
        _repositoryMock.Verify(r => r.GetByToken(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task ValidateToken_WithEmptyToken_ReturnsNull()
    {
        var result = await _service.ValidateToken("", "192.168.1.1");

        Assert.That(result, Is.Null);
        _repositoryMock.Verify(r => r.GetByToken(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task GetRateLimitsForScope_WithValidTokenAndScope_ReturnsLimits()
    {
        var token = new ApiToken
        {
            Token = "rate-limited",
            IsActive = true,
            Scopes = new Dictionary<string, ApiTokenScope>
            {
                ["replay"] = new ApiTokenScope
                {
                    HourlyLimit = 100,
                    DailyLimit = 1000,
                    IsEnabled = true
                }
            }
        };
        _repositoryMock.Setup(r => r.GetByToken("rate-limited"))
            .ReturnsAsync(token);

        var limits = await _service.GetRateLimitsForScope("rate-limited", "replay");

        Assert.That(limits, Is.Not.Null);
        Assert.That(limits.Value.hourlyLimit, Is.EqualTo(100));
        Assert.That(limits.Value.dailyLimit, Is.EqualTo(1000));
    }

    [Test]
    public async Task GetRateLimitsForScope_WithInvalidToken_ReturnsNull()
    {
        _repositoryMock.Setup(r => r.GetByToken("invalid"))
            .ReturnsAsync((ApiToken)null);

        var limits = await _service.GetRateLimitsForScope("invalid", "replay");

        Assert.That(limits, Is.Null);
    }

    [Test]
    public async Task GetRateLimitsForScope_WithMissingScope_ReturnsNull()
    {
        var token = new ApiToken
        {
            Token = "no-scope",
            IsActive = true,
            Scopes = new Dictionary<string, ApiTokenScope>()
        };
        _repositoryMock.Setup(r => r.GetByToken("no-scope"))
            .ReturnsAsync(token);

        var limits = await _service.GetRateLimitsForScope("no-scope", "replay");

        Assert.That(limits, Is.Null);
    }

    [Test]
    public async Task GetRateLimitsForScope_WithDisabledScope_ReturnsNull()
    {
        var token = new ApiToken
        {
            Token = "disabled-scope",
            IsActive = true,
            Scopes = new Dictionary<string, ApiTokenScope>
            {
                ["replay"] = new ApiTokenScope
                {
                    HourlyLimit = 100,
                    DailyLimit = 1000,
                    IsEnabled = false
                }
            }
        };
        _repositoryMock.Setup(r => r.GetByToken("disabled-scope"))
            .ReturnsAsync(token);

        var limits = await _service.GetRateLimitsForScope("disabled-scope", "replay");

        Assert.That(limits, Is.Null);
    }

    [Test]
    public async Task ValidateToken_WithNoIPRestrictions_AcceptsAnyIP()
    {
        var token = new ApiToken
        {
            Token = "no-ip-restriction",
            IsActive = true,
            AllowedIPs = Array.Empty<string>()
        };
        _repositoryMock.Setup(r => r.GetByToken("no-ip-restriction"))
            .ReturnsAsync(token);

        var result = await _service.ValidateToken("no-ip-restriction", "any.ip.address");

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task ValidateToken_UpdatesLastUsedEvenWhenCached()
    {
        var token = new ApiToken
        {
            Token = "update-test",
            IsActive = true
        };
        _repositoryMock.Setup(r => r.GetByToken("update-test"))
            .ReturnsAsync(token);

        // First call
        await _service.ValidateToken("update-test", null);

        // Second call (cached)
        await _service.ValidateToken("update-test", null);

        // UpdateLastUsed should be called twice even though token was cached
        _repositoryMock.Verify(r => r.UpdateLastUsed("update-test"), Times.Exactly(2));
    }
}
