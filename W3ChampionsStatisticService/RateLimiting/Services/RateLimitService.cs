using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.RateLimiting.Models;

namespace W3ChampionsStatisticService.RateLimiting.Services;

public class RateLimitContext
{
    public string PolicyName { get; set; }
    public int HourlyLimit { get; set; }
    public int DailyLimit { get; set; }
    public bool HasValidApiToken { get; set; }
    public ApiToken ApiToken { get; set; }
    public string PartitionKey { get; set; }
}

public interface IRateLimitService
{
    Task<RateLimitContext> DetermineRateLimitContext(HttpContext context, string scope, string policyName, int hourlyLimit, int dailyLimit);
    string GetIpAddress(HttpContext context);
}

public class RateLimitService(
    IApiTokenService apiTokenService,
    ILogger<RateLimitService> logger) : IRateLimitService
{
    private readonly IApiTokenService _apiTokenService = apiTokenService;
    private readonly ILogger<RateLimitService> _logger = logger;
    
    private const string API_TOKEN_HEADER = "X-API-Token";
    
    [Trace]
    public async Task<RateLimitContext> DetermineRateLimitContext(
        HttpContext context,
        string scope,
        string policyName, 
        int hourlyLimit, 
        int dailyLimit)
    {
        var ipAddress = GetIpAddress(context);
        
        // Check for API token first - if present, it overrides everything
        if (context.Request.Headers.TryGetValue(API_TOKEN_HEADER, out var apiTokenValue))
        {
            var apiToken = await _apiTokenService.ValidateToken(apiTokenValue.ToString(), ipAddress, scope);
            if (apiToken != null)
            {
                // Get scope-specific limits if available
                var tokenScope = apiToken.GetScope(scope);
                if (tokenScope != null && tokenScope.IsEnabled)
                {
                    _logger.LogDebug("Valid API token found with scope: {TokenId}, Scope: {Scope}, IP: {IP}", 
                        apiToken.Id, scope, ipAddress);
                    return new RateLimitContext
                    {
                        PolicyName = $"api-token-{scope}",
                        HourlyLimit = tokenScope.HourlyLimit,
                        DailyLimit = tokenScope.DailyLimit,
                        HasValidApiToken = true,
                        ApiToken = apiToken,
                        // Use token ID and scope as partition key so each token+scope combination has its own rate limit
                        PartitionKey = $"token:{apiToken.Id}:{scope}"
                    };
                }
                else
                {
                    _logger.LogWarning("API token lacks required scope: {TokenId}, Scope: {Scope}", apiToken.Id, scope);
                }
            }
            
            _logger.LogWarning("Invalid API token provided from IP: {IP} for scope: {Scope}", ipAddress, scope);
        }
        
        // Standard IP-based rate limiting with scope
        _logger.LogDebug("Using policy {PolicyName} with limits {HourlyLimit}/hr, {DailyLimit}/day for IP: {IP}, Scope: {Scope}", 
            policyName, hourlyLimit, dailyLimit, ipAddress, scope);
            
        return new RateLimitContext
        {
            PolicyName = policyName,
            HourlyLimit = hourlyLimit,
            DailyLimit = dailyLimit,
            HasValidApiToken = false,
            PartitionKey = $"ip:{ipAddress}:{scope}"
        };
    }
    
    public string GetIpAddress(HttpContext context)
    {
        // Check for Cloudflare's CF-Connecting-IP header first
        if (context.Request.Headers.TryGetValue("CF-Connecting-IP", out var cfIp))
        {
            return cfIp.ToString();
        }
        
        // Then check X-Forwarded-For
        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor))
        {
            // X-Forwarded-For can contain multiple IPs, take the first one
            var ips = forwardedFor.ToString().Split(',');
            if (ips.Length > 0)
            {
                return ips[0].Trim();
            }
        }
        
        // Fall back to remote IP address
        return context.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
    }
}