using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.RateLimiting.Models;
using W3ChampionsStatisticService.RateLimiting.Repositories;

namespace W3ChampionsStatisticService.RateLimiting.Services;

public interface IApiTokenService
{
    Task<ApiToken> ValidateToken(string token, string ipAddress, string scope = null);
    Task<(int hourlyLimit, int dailyLimit)?> GetRateLimitsForScope(string token, string scope);
}

public class ApiTokenService(
    IApiTokenRepository apiTokenRepository,
    IMemoryCache cache,
    ILogger<ApiTokenService> logger) : IApiTokenService
{
    private readonly IApiTokenRepository _apiTokenRepository = apiTokenRepository;
    private readonly IMemoryCache _cache = cache;
    private readonly ILogger<ApiTokenService> _logger = logger;
    private const int CACHE_DURATION_MINUTES = 5;

    [Trace]
    public async Task<ApiToken> ValidateToken(string token, string ipAddress, string scope = null)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        // Check cache first
        var cacheKey = $"apitoken:{token}";
        if (_cache.TryGetValue<ApiToken>(cacheKey, out var cachedToken))
        {
            if (ValidateTokenInternal(cachedToken, ipAddress, scope))
            {
                // Update last used asynchronously (fire and forget)
                _ = _apiTokenRepository.UpdateLastUsed(token);
                return cachedToken;
            }
            return null;
        }

        // Load from database
        var apiToken = await _apiTokenRepository.GetByToken(token);
        if (apiToken == null)
        {
            _logger.LogWarning("Invalid API token attempted: {Token}", token);
            return null;
        }

        // Validate
        if (!ValidateTokenInternal(apiToken, ipAddress, scope))
        {
            return null;
        }

        // Cache the valid token
        _cache.Set(cacheKey, apiToken, TimeSpan.FromMinutes(CACHE_DURATION_MINUTES));

        // Update last used asynchronously (fire and forget)
        _ = _apiTokenRepository.UpdateLastUsed(token);

        return apiToken;
    }

    public async Task<(int hourlyLimit, int dailyLimit)?> GetRateLimitsForScope(string token, string scope)
    {
        var apiToken = await ValidateToken(token, null, scope);
        if (apiToken == null)
            return null;

        var tokenScope = apiToken.GetScope(scope);
        if (tokenScope == null || !tokenScope.IsEnabled)
            return null;

        return (tokenScope.HourlyLimit, tokenScope.DailyLimit);
    }

    private bool ValidateTokenInternal(ApiToken apiToken, string ipAddress, string scope = null)
    {
        if (!apiToken.IsActive)
        {
            _logger.LogInformation("Inactive API token used: {TokenId}", apiToken.Id);
            return false;
        }

        if (apiToken.IsExpired())
        {
            _logger.LogInformation("Expired API token used: {TokenId}, ExpiredAt: {ExpiredAt}",
                apiToken.Id, apiToken.ExpiresAt);
            return false;
        }

        if (!string.IsNullOrEmpty(ipAddress) && !apiToken.IsIpAllowed(ipAddress))
        {
            _logger.LogWarning("API token used from unauthorized IP: {TokenId}, IP: {IP}",
                apiToken.Id, ipAddress);
            return false;
        }

        // Check scope if provided
        if (!string.IsNullOrEmpty(scope))
        {
            if (!apiToken.HasScope(scope))
            {
                _logger.LogWarning("API token used without required scope: {TokenId}, Scope: {Scope}",
                    apiToken.Id, scope);
                return false;
            }

            var tokenScope = apiToken.GetScope(scope);
            if (tokenScope == null || !tokenScope.IsEnabled)
            {
                _logger.LogWarning("API token used with disabled scope: {TokenId}, Scope: {Scope}",
                    apiToken.Id, scope);
                return false;
            }
        }

        return true;
    }
}
