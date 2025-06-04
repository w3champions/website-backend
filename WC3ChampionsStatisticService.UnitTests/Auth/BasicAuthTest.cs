using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;
using System;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using W3ChampionsStatisticService.WebApi.Authorization;

namespace WC3ChampionsStatisticService.Tests.Auth;

[TestFixture]
public class BasicAuthTest
{
    private MetricsBasicAuthHandler _handler;
    private MetricsBasicAuthRequirement _requirement;

    [SetUp]
    public void Setup()
    {
        _handler = new MetricsBasicAuthHandler();
        _requirement = new MetricsBasicAuthRequirement();
    }

    [Test]
    public async Task BasicAuth_ValidCredentials_Success()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:admin"));
        httpContext.Request.Headers["Authorization"] = $"Basic {credentials}";

        var context = new AuthorizationHandlerContext(
            new[] { _requirement },
            new ClaimsPrincipal(),
            httpContext);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        Assert.IsTrue(context.HasSucceeded);
        Assert.IsTrue(httpContext.User.Identity.IsAuthenticated);
        Assert.AreEqual("BasicAuthentication", httpContext.User.Identity.AuthenticationType);
    }

    [Test]
    public async Task BasicAuth_InvalidCredentials_Failure()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("wrong:password"));
        httpContext.Request.Headers["Authorization"] = $"Basic {credentials}";

        var context = new AuthorizationHandlerContext(
            new[] { _requirement },
            new ClaimsPrincipal(),
            httpContext);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        Assert.IsFalse(context.HasSucceeded);
        Assert.IsFalse(httpContext.User.Identity.IsAuthenticated);
    }

    [Test]
    public async Task BasicAuth_BearerToken_FailsSilently()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["Authorization"] = "Bearer some-jwt-token";

        var context = new AuthorizationHandlerContext(
            new[] { _requirement },
            new ClaimsPrincipal(),
            httpContext);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        Assert.IsFalse(context.HasSucceeded);
        Assert.IsFalse(httpContext.User.Identity.IsAuthenticated);
        // This should fail silently without logging warnings about Bearer tokens
    }

    [Test]
    public async Task BasicAuth_NoAuthHeader_Failure()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();

        var context = new AuthorizationHandlerContext(
            new[] { _requirement },
            new ClaimsPrincipal(),
            httpContext);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        Assert.IsFalse(context.HasSucceeded);
        Assert.IsFalse(httpContext.User.Identity.IsAuthenticated);
    }

    [Test]
    public async Task BasicAuth_CustomCredentials_Success()
    {
        // Arrange
        Environment.SetEnvironmentVariable("METRICS_ENDPOINT_AUTH_USERNAME", "testuser");
        Environment.SetEnvironmentVariable("METRICS_ENDPOINT_AUTH_PASSWORD", "testpass");

        var httpContext = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("testuser:testpass"));
        httpContext.Request.Headers["Authorization"] = $"Basic {credentials}";

        var context = new AuthorizationHandlerContext(
            new[] { _requirement },
            new ClaimsPrincipal(),
            httpContext);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        Assert.IsTrue(context.HasSucceeded);
        Assert.IsTrue(httpContext.User.Identity.IsAuthenticated);

        // Cleanup
        Environment.SetEnvironmentVariable("METRICS_ENDPOINT_AUTH_USERNAME", null);
        Environment.SetEnvironmentVariable("METRICS_ENDPOINT_AUTH_PASSWORD", null);
    }
} 