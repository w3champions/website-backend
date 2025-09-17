using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.RateLimiting.Services;

namespace W3ChampionsStatisticService.WebApi.ActionFilters;

[AttributeUsage(AttributeTargets.Method)]
public class ReplayRateLimitAttribute : RateLimitAttribute
{

    /// <summary>
    /// Hourly limit for older matches (> threshold days)
    /// </summary>
    public int StrictHourlyLimit { get; set; } = 10;

    /// <summary>
    /// Daily limit for older matches (> threshold days)
    /// </summary>
    public int StrictDailyLimit { get; set; } = 50;

    /// <summary>
    /// Hourly limit for recent matches (<= threshold days)
    /// </summary>
    public int RelaxedHourlyLimit { get; set; } = 30;

    /// <summary>
    /// Daily limit for recent matches (<= threshold days)
    /// </summary>
    public int RelaxedDailyLimit { get; set; } = 100;

    /// <summary>
    /// Threshold in days to determine if a match is recent
    /// </summary>
    public int MatchAgeThresholdDays { get; set; } = 7;

    public ReplayRateLimitAttribute()
    {
        // Set default scope for replay endpoints
        Scope = "replay";
    }

    protected override async Task<RateLimitContext> DetermineRateLimitContext(
        ActionExecutingContext context,
        IRateLimitService rateLimitService)
    {
        var matchRepository = context.HttpContext.RequestServices.GetRequiredService<IMatchRepository>();
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<ReplayRateLimitAttribute>>();

        // Default to strict limits
        int hourlyLimit = StrictHourlyLimit;
        int dailyLimit = StrictDailyLimit;
        string policyName = "replay-strict";

        // Check for gameId parameter
        string gameId = null;
        int? floMatchId = null;

        if (context.ActionArguments.TryGetValue("gameId", out var gameIdValue))
        {
            gameId = gameIdValue?.ToString();
        }
        else if (context.ActionArguments.TryGetValue("floMatchId", out var floMatchIdValue))
        {
            if (floMatchIdValue != null && int.TryParse(floMatchIdValue.ToString(), out var floId))
            {
                floMatchId = floId;
            }
        }

        // Check match age
        bool? isRecent = await CheckMatchAge(matchRepository, gameId, floMatchId, MatchAgeThresholdDays, logger);

        if (isRecent.HasValue)
        {
            if (isRecent.Value)
            {
                hourlyLimit = RelaxedHourlyLimit;
                dailyLimit = RelaxedDailyLimit;
                policyName = "replay-relaxed";
                logger.LogDebug("Match is recent (within {Days} days), using relaxed limits", MatchAgeThresholdDays);
            }
            else
            {
                logger.LogDebug("Match is old (> {Days} days), using strict limits", MatchAgeThresholdDays);
            }
        }
        else
        {
            logger.LogDebug("Could not determine match age, using strict limits by default");
        }

        // Get the rate limit context (checks for API tokens, gets IP, etc.)
        return await rateLimitService.DetermineRateLimitContext(
            context.HttpContext,
            Scope,
            policyName,
            hourlyLimit,
            dailyLimit);
    }

    private async Task<bool?> CheckMatchAge(IMatchRepository matchRepository, string gameId, int? floMatchId, int thresholdDays, ILogger logger)
    {
        try
        {
            MatchupDetail matchDetail = null;

            if (!string.IsNullOrEmpty(gameId))
            {
                matchDetail = await matchRepository.LoadFinishedMatchDetailsByMatchId(gameId);
            }
            else if (floMatchId.HasValue && floMatchId.Value > 0)
            {
                matchDetail = await matchRepository.LoadFinishedMatchDetailsByFloId(floMatchId.Value);
            }

            if (matchDetail?.Match != null)
            {
                var matchAge = DateTimeOffset.UtcNow - matchDetail.Match.EndTime;
                return matchAge.TotalDays <= thresholdDays;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking match age for gameId: {GameId}, floMatchId: {FloMatchId}", gameId, floMatchId);
        }

        return null; // Could not determine
    }
}
