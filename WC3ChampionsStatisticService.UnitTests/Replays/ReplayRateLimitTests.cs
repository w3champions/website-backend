using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using W3C.Contracts.Admin.Permission;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.RateLimiting.Models;
using W3ChampionsStatisticService.RateLimiting.Services;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace WC3ChampionsStatisticService.Tests.Replays;

[TestFixture]
public class ReplayRateLimitTests
{
    private Mock<IRateLimitService> _rateLimitServiceMock;
    private Mock<IRateLimitBucketService> _bucketServiceMock;
    private Mock<IMatchRepository> _matchRepositoryMock;
    private Mock<IW3CAuthenticationService> _authServiceMock;
    private ReplayRateLimitAttribute _attribute;
    private ActionExecutingContext _context;
    private HttpContext _httpContext;

    private const string StrictPolicy = "replay-strict";
    private const string ModeratorPolicy = "replay-moderator";

    [SetUp]
    public void Setup()
    {
        _rateLimitServiceMock = new Mock<IRateLimitService>();
        _bucketServiceMock = new Mock<IRateLimitBucketService>();
        _matchRepositoryMock = new Mock<IMatchRepository>();
        _authServiceMock = new Mock<IW3CAuthenticationService>();

        var services = new ServiceCollection();
        services.AddSingleton(_rateLimitServiceMock.Object);
        services.AddSingleton(_bucketServiceMock.Object);
        services.AddSingleton(_matchRepositoryMock.Object);
        services.AddSingleton(_authServiceMock.Object);
        services.AddSingleton(Mock.Of<ILogger<ReplayRateLimitAttribute>>());
        services.AddSingleton(Mock.Of<ILogger<RateLimitAttribute>>());

        var serviceProvider = services.BuildServiceProvider();

        _httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };
        _httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.168.1.1");

        var actionContext = new ActionContext(
            _httpContext,
            new RouteData(),
            new ActionDescriptor());

        _context = new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object>(),
            Mock.Of<Controller>());

        _attribute = new ReplayRateLimitAttribute();

        // A lease that always succeeds - we only care about the RateLimitContext that gets
        // computed and stored on HttpContext.Items, not the bucket accounting itself.
        var lease = new Mock<RateLimitLease>();
        lease.Setup(l => l.IsAcquired).Returns(true);
        _bucketServiceMock
            .Setup(b => b.TryAcquireAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(lease.Object);
    }

    private RateLimitContext SetupBaseContext(bool hasValidApiToken = false, int hourlyLimit = 10, int dailyLimit = 50, string partitionKey = "ip:192.168.1.1:replay")
    {
        var baseContext = new RateLimitContext
        {
            PolicyName = StrictPolicy,
            HourlyLimit = hourlyLimit,
            DailyLimit = dailyLimit,
            HasValidApiToken = hasValidApiToken,
            PartitionKey = partitionKey
        };

        _rateLimitServiceMock
            .Setup(s => s.DetermineRateLimitContext(_httpContext, "replay", StrictPolicy, 10, 50))
            .ReturnsAsync(baseContext);

        return baseContext;
    }

    private async Task<RateLimitContext> Invoke()
    {
        var next = new ActionExecutionDelegate(() => Task.FromResult(
            new ActionExecutedContext(
                new ActionContext(_httpContext, new RouteData(), new ActionDescriptor()),
                new List<IFilterMetadata>(),
                Mock.Of<Controller>())));

        await _attribute.OnActionExecutionAsync(_context, next);

        return _httpContext.Items["RateLimitContext"] as RateLimitContext;
    }

    [Test]
    public async Task ModeratorToken_RaisesHourlyLimit_KeepsDailyLimit_UsesModeratorPartition()
    {
        SetupBaseContext();

        const string battleTag = "Moderator#123";
        _httpContext.Request.Headers["Authorization"] = "Bearer valid-moderator-token";
        _authServiceMock
            .Setup(a => a.GetUserByToken("valid-moderator-token", true))
            .Returns(new W3CUserAuthenticationDto
            {
                BattleTag = battleTag,
                IsAdmin = true,
                Permissions = new HashSet<EPermission> { EPermission.Moderation }
            });

        var result = await Invoke();

        Assert.That(result, Is.Not.Null);
        Assert.That(result.HourlyLimit, Is.EqualTo(50));
        Assert.That(result.DailyLimit, Is.EqualTo(50));
        Assert.That(result.PolicyName, Is.EqualTo(ModeratorPolicy));
        Assert.That(result.PartitionKey, Is.EqualTo($"moderator:{battleTag}:replay"));
    }

    [Test]
    public async Task ValidTokenWithoutModerationPermission_LeavesLimitsUnchanged()
    {
        SetupBaseContext();

        _httpContext.Request.Headers["Authorization"] = "Bearer valid-non-moderator-token";
        _authServiceMock
            .Setup(a => a.GetUserByToken("valid-non-moderator-token", true))
            .Returns(new W3CUserAuthenticationDto
            {
                BattleTag = "Player#456",
                IsAdmin = true,
                Permissions = new HashSet<EPermission>()
            });

        var result = await Invoke();

        Assert.That(result, Is.Not.Null);
        Assert.That(result.HourlyLimit, Is.EqualTo(10));
        Assert.That(result.DailyLimit, Is.EqualTo(50));
        Assert.That(result.PartitionKey, Is.EqualTo("ip:192.168.1.1:replay"));
    }

    [Test]
    public async Task NoAuthorizationHeader_LeavesLimitsUnchanged()
    {
        SetupBaseContext();

        var result = await Invoke();

        Assert.That(result, Is.Not.Null);
        Assert.That(result.HourlyLimit, Is.EqualTo(10));
        Assert.That(result.DailyLimit, Is.EqualTo(50));
        Assert.That(result.PartitionKey, Is.EqualTo("ip:192.168.1.1:replay"));
        _authServiceMock.Verify(a => a.GetUserByToken(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Test]
    public async Task MalformedToken_FallsBackToIpBasedLimit()
    {
        SetupBaseContext();

        _httpContext.Request.Headers["Authorization"] = "Bearer garbage-token";
        _authServiceMock
            .Setup(a => a.GetUserByToken("garbage-token", true))
            .Throws(new Microsoft.IdentityModel.Tokens.SecurityTokenValidationException("bad token"));

        var result = await Invoke();

        Assert.That(result, Is.Not.Null);
        Assert.That(result.HourlyLimit, Is.EqualTo(10));
        Assert.That(result.DailyLimit, Is.EqualTo(50));
        Assert.That(result.PartitionKey, Is.EqualTo("ip:192.168.1.1:replay"));
    }

    [Test]
    public async Task NonAdminWithModerationPermission_LeavesLimitsUnchanged()
    {
        // Regression coverage: holding EPermission.Moderation is not sufficient on its
        // own. BearerHasPermissionFilter's own check requires IsAdmin == true as well,
        // and ReplayRateLimitAttribute must copy that exactly.
        SetupBaseContext();

        _httpContext.Request.Headers["Authorization"] = "Bearer valid-non-admin-token";
        _authServiceMock
            .Setup(a => a.GetUserByToken("valid-non-admin-token", true))
            .Returns(new W3CUserAuthenticationDto
            {
                BattleTag = "NotAnAdmin#789",
                IsAdmin = false,
                Permissions = new HashSet<EPermission> { EPermission.Moderation }
            });

        var result = await Invoke();

        Assert.That(result, Is.Not.Null);
        Assert.That(result.HourlyLimit, Is.EqualTo(10));
        Assert.That(result.DailyLimit, Is.EqualTo(50));
        Assert.That(result.PolicyName, Is.EqualTo(StrictPolicy));
        Assert.That(result.PartitionKey, Is.EqualTo("ip:192.168.1.1:replay"));
    }

    [Test]
    public async Task ModeratorOnRecentMatch_KeepsHigherRelaxedLimit()
    {
        // The relaxed policy can already grant an hourly limit higher than
        // ModeratorHourlyLimit. The override must raise, never lower, the limit.
        const int relaxedHourlyLimit = 100;
        const int relaxedDailyLimit = 150;
        _attribute.RelaxedHourlyLimit = relaxedHourlyLimit;
        _attribute.RelaxedDailyLimit = relaxedDailyLimit;

        _context.ActionArguments["gameId"] = "recent-game-id";
        _matchRepositoryMock
            .Setup(m => m.LoadFinishedMatchDetailsByMatchId("recent-game-id"))
            .ReturnsAsync(new MatchupDetail
            {
                Match = new Matchup { EndTime = DateTimeOffset.UtcNow.AddDays(-1) }
            });

        var relaxedContext = new RateLimitContext
        {
            PolicyName = "replay-relaxed",
            HourlyLimit = relaxedHourlyLimit,
            DailyLimit = relaxedDailyLimit,
            HasValidApiToken = false,
            PartitionKey = "ip:192.168.1.1:replay"
        };
        _rateLimitServiceMock
            .Setup(s => s.DetermineRateLimitContext(_httpContext, "replay", "replay-relaxed", relaxedHourlyLimit, relaxedDailyLimit))
            .ReturnsAsync(relaxedContext);

        const string battleTag = "Moderator#123";
        _httpContext.Request.Headers["Authorization"] = "Bearer valid-moderator-token";
        _authServiceMock
            .Setup(a => a.GetUserByToken("valid-moderator-token", true))
            .Returns(new W3CUserAuthenticationDto
            {
                BattleTag = battleTag,
                IsAdmin = true,
                Permissions = new HashSet<EPermission> { EPermission.Moderation }
            });

        var result = await Invoke();

        Assert.That(result, Is.Not.Null);
        // Must stay at the relaxed 100, NOT be pulled down to ModeratorHourlyLimit (50).
        Assert.That(result.HourlyLimit, Is.EqualTo(relaxedHourlyLimit));
        Assert.That(result.DailyLimit, Is.EqualTo(relaxedDailyLimit));
        Assert.That(result.PolicyName, Is.EqualTo(ModeratorPolicy));
        Assert.That(result.PartitionKey, Is.EqualTo($"moderator:{battleTag}:replay"));
    }

    [Test]
    public async Task ValidApiToken_WinsOverModeratorOverride()
    {
        var apiToken = new ApiToken
        {
            Token = "test-api-token",
            Name = "Test Token",
            Scopes = new Dictionary<string, ApiTokenScope>
            {
                ["replay"] = new ApiTokenScope { HourlyLimit = 200, DailyLimit = 2000, IsEnabled = true }
            }
        };

        _rateLimitServiceMock
            .Setup(s => s.DetermineRateLimitContext(_httpContext, "replay", StrictPolicy, 10, 50))
            .ReturnsAsync(new RateLimitContext
            {
                PolicyName = StrictPolicy,
                HourlyLimit = 200,
                DailyLimit = 2000,
                HasValidApiToken = true,
                ApiToken = apiToken,
                PartitionKey = "token:test-api-token:replay"
            });

        _httpContext.Request.Headers["X-API-Token"] = "test-api-token";
        // Even a moderator bearer token must not override an already-valid API token.
        _httpContext.Request.Headers["Authorization"] = "Bearer valid-moderator-token";
        _authServiceMock
            .Setup(a => a.GetUserByToken("valid-moderator-token", true))
            .Returns(new W3CUserAuthenticationDto
            {
                BattleTag = "Moderator#123",
                IsAdmin = true,
                Permissions = new HashSet<EPermission> { EPermission.Moderation }
            });

        var result = await Invoke();

        Assert.That(result, Is.Not.Null);
        Assert.That(result.HourlyLimit, Is.EqualTo(200));
        Assert.That(result.DailyLimit, Is.EqualTo(2000));
        Assert.That(result.PartitionKey, Is.EqualTo("token:test-api-token:replay"));
        _authServiceMock.Verify(a => a.GetUserByToken(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }
}
