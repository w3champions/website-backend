using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Services;

public interface ITurnstileService
{
    Task<bool> VerifyTokenAsync(string token, string remoteIp = null);
    Task<TurnstileVerificationResult> VerifyTokenAsync(string token, string remoteIp = null, int? maxAgeSeconds = null);
    bool IsEnabled { get; }
}

public class TurnstileVerificationResult
{
    public bool IsValid { get; set; }
    public DateTime? ChallengeTimestamp { get; set; }
    public bool IsExpiredByAge { get; set; }
    public string ErrorMessage { get; set; }
    
    /// <summary>
    /// Creates a successful verification result
    /// </summary>
    public static TurnstileVerificationResult Success(DateTime? challengeTimestamp)
    {
        return new TurnstileVerificationResult
        {
            IsValid = true,
            ChallengeTimestamp = challengeTimestamp,
            IsExpiredByAge = false,
            ErrorMessage = null
        };
    }
    
    /// <summary>
    /// Creates a failed verification result
    /// </summary>
    public static TurnstileVerificationResult Failed(string errorMessage)
    {
        return new TurnstileVerificationResult
        {
            IsValid = false,
            ChallengeTimestamp = null,
            IsExpiredByAge = false,
            ErrorMessage = errorMessage
        };
    }
    
    /// <summary>
    /// Creates a result for token that is too old
    /// </summary>
    public static TurnstileVerificationResult ExpiredByAge(DateTime challengeTimestamp)
    {
        return new TurnstileVerificationResult
        {
            IsValid = false,
            ChallengeTimestamp = challengeTimestamp,
            IsExpiredByAge = true,
            ErrorMessage = "Token has expired. Please refresh and try again."
        };
    }
}

public class TurnstileVerificationException : Exception
{
    public TurnstileVerificationException(string message) : base(message) { }
    public TurnstileVerificationException(string message, Exception innerException) : base(message, innerException) { }
}

public class TurnstileService : ITurnstileService
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TurnstileService> _logger;
    private readonly string _secretKey;
    private const string TURNSTILE_VERIFY_URL = "https://challenges.cloudflare.com/turnstile/v0/siteverify";
    private const int CACHE_DURATION_MINUTES = 5;

    public bool IsEnabled { get; }

    public TurnstileService(HttpClient httpClient, IMemoryCache cache, ILogger<TurnstileService> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _logger = logger;
        _secretKey = Environment.GetEnvironmentVariable("TURNSTILE_SECRET_KEY");
        IsEnabled = !string.IsNullOrEmpty(_secretKey);

        if (!IsEnabled)
        {
            _logger.LogWarning("TURNSTILE_SECRET_KEY is not set. Turnstile verification is disabled.");
        }
    }

    [Trace]
    public async Task<bool> VerifyTokenAsync(string token, string remoteIp = null)
    {
        var result = await VerifyTokenAsync(token, remoteIp, null);
        return result.IsValid;
    }
    
    [Trace]
    public async Task<TurnstileVerificationResult> VerifyTokenAsync(string token, string remoteIp = null, int? maxAgeSeconds = null)
    {
        // Skip verification if not enabled
        if (!IsEnabled)
        {
            _logger.LogDebug("Turnstile verification is disabled (no secret key configured)");
            return TurnstileVerificationResult.Success(null);
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogDebug("Turnstile token is empty or null");
            return TurnstileVerificationResult.Failed("Token is missing");
        }

        // Check cache first
        var cacheKey = $"turnstile_token_{token}";
        if (_cache.TryGetValue<TurnstileVerificationResult>(cacheKey, out var cachedResult))
        {
            _logger.LogDebug("Turnstile token found in cache");
            
            // Check age if max age is specified and we have a cached timestamp
            if (maxAgeSeconds.HasValue && cachedResult.ChallengeTimestamp.HasValue)
            {
                var ageSeconds = (DateTime.UtcNow - cachedResult.ChallengeTimestamp.Value).TotalSeconds;
                if (ageSeconds > maxAgeSeconds.Value)
                {
                    _logger.LogInformation($"Cached token exceeds max age. Age: {ageSeconds:F0} seconds, max allowed: {maxAgeSeconds.Value} seconds, IP: {remoteIp}");
                    return TurnstileVerificationResult.ExpiredByAge(cachedResult.ChallengeTimestamp.Value);
                }
            }
            
            return cachedResult;
        }

        try
        {
            var formData = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("secret", _secretKey),
                new KeyValuePair<string, string>("response", token),
                new KeyValuePair<string, string>("remoteip", remoteIp ?? string.Empty)
            });

            var response = await _httpClient.PostAsync(TURNSTILE_VERIFY_URL, formData);

            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                throw new TurnstileVerificationException(
                    $"Turnstile API returned status code {response.StatusCode}: {content}");
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var result = JsonSerializer.Deserialize<TurnstileResponse>(jsonResponse, options);

            if (result == null)
            {
                throw new TurnstileVerificationException("Failed to deserialize Turnstile response");
            }

            if (result.Success)
            {
                var verificationResult = TurnstileVerificationResult.Success(result.ChallengeTs);
                
                // Check age if max age is specified
                if (maxAgeSeconds.HasValue && result.ChallengeTs.HasValue)
                {
                    var ageSeconds = (DateTime.UtcNow - result.ChallengeTs.Value).TotalSeconds;
                    if (ageSeconds > maxAgeSeconds.Value)
                    {
                        _logger.LogInformation($"Token exceeds max age. Age: {ageSeconds:F0} seconds, max allowed: {maxAgeSeconds.Value} seconds, IP: {remoteIp}");
                        return TurnstileVerificationResult.ExpiredByAge(result.ChallengeTs.Value);
                    }
                }
                
                // Cache successful verification
                _cache.Set(cacheKey, verificationResult, TimeSpan.FromMinutes(CACHE_DURATION_MINUTES));
                _logger.LogDebug($"Turnstile token verified successfully. IP: {remoteIp}");
                return verificationResult;
            }

            // Token verification failed (invalid token, expired, already used, etc.)
            if (result.ErrorCodes != null && result.ErrorCodes.Count > 0)
            {
                _logger.LogDebug($"Turnstile verification failed: {string.Join(", ", result.ErrorCodes)}, IP: {remoteIp}");
            }

            return TurnstileVerificationResult.Failed("Token verification failed");
        }
        catch (HttpRequestException ex)
        {
            throw new TurnstileVerificationException("Failed to connect to Turnstile API", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new TurnstileVerificationException("Turnstile API request timed out", ex);
        }
        catch (TurnstileVerificationException)
        {
            throw;  // Re-throw our custom exceptions
        }
        catch (Exception ex)
        {
            throw new TurnstileVerificationException("Unexpected error during Turnstile verification", ex);
        }
    }

    private class TurnstileResponse
    {
        public bool Success { get; set; }

        [JsonPropertyName("challenge_ts")]
        public DateTime? ChallengeTs { get; set; }

        public string Hostname { get; set; }

        [JsonPropertyName("error-codes")]
        public List<string> ErrorCodes { get; set; }

        public string Action { get; set; }

        public string CData { get; set; }
    }
}
