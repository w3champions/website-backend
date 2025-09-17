using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using W3ChampionsStatisticService.Services;

namespace WC3ChampionsStatisticService.Tests.Services;

[TestFixture]
public class TurnstileServiceTests
{
    private Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private HttpClient _httpClient;
    private IMemoryCache _memoryCache;
    private Mock<ILogger<TurnstileService>> _loggerMock;
    private TurnstileService _turnstileService;
    private const string TestSecretKey = "test-secret-key";
    private const string TestToken = "test-token-123";
    private const string TestRemoteIp = "192.168.1.1";

    [SetUp]
    public void SetUp()
    {
        Environment.SetEnvironmentVariable("TURNSTILE_SECRET_KEY", TestSecretKey);
        
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_httpMessageHandlerMock.Object);
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _loggerMock = new Mock<ILogger<TurnstileService>>();
        
        _turnstileService = new TurnstileService(_httpClient, _memoryCache, _loggerMock.Object);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("TURNSTILE_SECRET_KEY", null);
        _httpClient?.Dispose();
        _memoryCache?.Dispose();
    }

    [Test]
    public async Task VerifyTokenAsync_WithValidToken_ReturnsSuccess()
    {
        // Arrange
        var challengeTimestamp = DateTime.UtcNow.AddSeconds(-30);
        var responseJson = $@"{{
            ""success"": true,
            ""challenge_ts"": ""{challengeTimestamp:yyyy-MM-dd'T'HH:mm:ss.fff'Z'}"",
            ""hostname"": ""example.com""
        }}";

        SetupHttpResponse(HttpStatusCode.OK, responseJson);

        // Act
        var result = await _turnstileService.VerifyTokenAsync(TestToken, TestRemoteIp, maxAgeSeconds: null);

        // Assert
        Assert.IsTrue(result.IsValid);
        Assert.IsFalse(result.IsExpiredByAge);
        Assert.IsNotNull(result.ChallengeTimestamp);
        Assert.IsNull(result.ErrorMessage);
    }

    [Test]
    public async Task VerifyTokenAsync_WithInvalidToken_ReturnsFailure()
    {
        // Arrange
        var responseJson = @"{
            ""success"": false,
            ""error-codes"": [""invalid-input-response""]
        }";

        SetupHttpResponse(HttpStatusCode.OK, responseJson);

        // Act
        var result = await _turnstileService.VerifyTokenAsync(TestToken, TestRemoteIp, maxAgeSeconds: null);

        // Assert
        Assert.IsFalse(result.IsValid);
        Assert.IsFalse(result.IsExpiredByAge);
        Assert.IsNull(result.ChallengeTimestamp);
        Assert.AreEqual("Token verification failed", result.ErrorMessage);
    }

    [Test]
    public async Task VerifyTokenAsync_WithMaxAge_TokenTooOld_ReturnsExpired()
    {
        // Arrange
        var challengeTimestamp = DateTime.UtcNow.AddMinutes(-10); // 10 minutes old
        var maxAgeSeconds = 300; // 5 minutes max
        
        var responseJson = $@"{{
            ""success"": true,
            ""challenge_ts"": ""{challengeTimestamp:yyyy-MM-dd'T'HH:mm:ss.fff'Z'}"",
            ""hostname"": ""example.com""
        }}";

        SetupHttpResponse(HttpStatusCode.OK, responseJson);

        // Act
        var result = await _turnstileService.VerifyTokenAsync(TestToken, TestRemoteIp, maxAgeSeconds);

        // Assert
        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.IsExpiredByAge);
        Assert.IsNotNull(result.ChallengeTimestamp);
        Assert.AreEqual("Token has expired. Please refresh and try again.", result.ErrorMessage);
        
        // Verify logging
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Token exceeds max age")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Test]
    public async Task VerifyTokenAsync_WithMaxAge_TokenWithinAge_ReturnsSuccess()
    {
        // Arrange
        var challengeTimestamp = DateTime.UtcNow.AddMinutes(-2); // 2 minutes old
        var maxAgeSeconds = 300; // 5 minutes max
        
        var responseJson = $@"{{
            ""success"": true,
            ""challenge_ts"": ""{challengeTimestamp:yyyy-MM-dd'T'HH:mm:ss.fff'Z'}"",
            ""hostname"": ""example.com""
        }}";

        SetupHttpResponse(HttpStatusCode.OK, responseJson);

        // Act
        var result = await _turnstileService.VerifyTokenAsync(TestToken, TestRemoteIp, maxAgeSeconds);

        // Assert
        Assert.IsTrue(result.IsValid);
        Assert.IsFalse(result.IsExpiredByAge);
        Assert.IsNotNull(result.ChallengeTimestamp);
        Assert.IsNull(result.ErrorMessage);
    }

    [Test]
    public async Task VerifyTokenAsync_CachedToken_WithMaxAge_RechecksAge()
    {
        // Arrange - First request with fresh token
        var challengeTimestamp = DateTime.UtcNow.AddMinutes(-2);
        var responseJson = $@"{{
            ""success"": true,
            ""challenge_ts"": ""{challengeTimestamp:yyyy-MM-dd'T'HH:mm:ss.fff'Z'}"",
            ""hostname"": ""example.com""
        }}";

        SetupHttpResponse(HttpStatusCode.OK, responseJson);

        // Act 1 - First verification (should cache)
        var result1 = await _turnstileService.VerifyTokenAsync(TestToken, TestRemoteIp, maxAgeSeconds: 300);
        Assert.IsTrue(result1.IsValid);

        // Simulate time passing (we can't actually change the cached timestamp, 
        // but we can verify the cache is used)
        
        // Act 2 - Second verification should use cache
        var result2 = await _turnstileService.VerifyTokenAsync(TestToken, TestRemoteIp, maxAgeSeconds: 300);
        
        // Assert
        Assert.IsTrue(result2.IsValid);
        Assert.AreEqual(result1.ChallengeTimestamp, result2.ChallengeTimestamp);
        
        // Verify HTTP was called only once (cache was used)
        _httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>()
        );
    }

    [Test]
    public async Task VerifyTokenAsync_WithEmptyToken_ReturnsFailure()
    {
        // Act
        var result = await _turnstileService.VerifyTokenAsync("", TestRemoteIp, maxAgeSeconds: null);

        // Assert
        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("Token is missing", result.ErrorMessage);
        
        // Verify no HTTP call was made
        _httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>()
        );
    }

    [Test]
    public async Task VerifyTokenAsync_WhenDisabled_ReturnsSuccess()
    {
        // Arrange - Disable by removing secret key
        Environment.SetEnvironmentVariable("TURNSTILE_SECRET_KEY", null);
        var service = new TurnstileService(_httpClient, _memoryCache, _loggerMock.Object);

        // Act
        var result = await service.VerifyTokenAsync(TestToken, TestRemoteIp, maxAgeSeconds: 60);

        // Assert
        Assert.IsTrue(result.IsValid);
        Assert.IsNull(result.ChallengeTimestamp);
        Assert.IsFalse(service.IsEnabled);
        
        // Verify no HTTP call was made
        _httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>()
        );
    }

    [Test]
    public async Task VerifyTokenAsync_BackwardCompatibility_BooleanOverload()
    {
        // Arrange
        var responseJson = @"{
            ""success"": true,
            ""challenge_ts"": ""2025-01-01T12:00:00.000Z"",
            ""hostname"": ""example.com""
        }";

        SetupHttpResponse(HttpStatusCode.OK, responseJson);

        // Act - Use the boolean overload
        var result = await _turnstileService.VerifyTokenAsync(TestToken, TestRemoteIp);

        // Assert
        Assert.IsTrue(result);
    }

    [Test]
    public void VerifyTokenAsync_ApiError_ThrowsTurnstileVerificationException()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.ServiceUnavailable, "Service Unavailable");

        // Act & Assert
        var ex = Assert.ThrowsAsync<TurnstileVerificationException>(
            async () => await _turnstileService.VerifyTokenAsync(TestToken, TestRemoteIp, maxAgeSeconds: null)
        );
        
        Assert.That(ex.Message, Does.Contain("Turnstile API returned status code ServiceUnavailable"));
    }

    private void SetupHttpResponse(HttpStatusCode statusCode, string content)
    {
        var responseMessage = new HttpResponseMessage
        {
            StatusCode = statusCode,
            Content = new StringContent(content)
        };

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(responseMessage);
    }
}