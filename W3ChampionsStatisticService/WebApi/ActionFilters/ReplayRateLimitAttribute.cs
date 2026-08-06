using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using W3C.Contracts.Admin.Permission;
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

    /// <summary>
    /// Hourly limit for authenticated moderators. The daily limit deliberately stays
    /// at the strict/relaxed value, so a moderator can spend a day's allowance in one
    /// burst but not exceed it.
    /// </summary>
    public int ModeratorHourlyLimit { get; set; } = 50;

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
        var rateLimitContext = await rateLimitService.DetermineRateLimitContext(
            context.HttpContext,
            Scope,
            policyName,
            hourlyLimit,
            dailyLimit);

        // An API token already carries its own negotiated limits; never override them.
        if (rateLimitContext.HasValidApiToken)
        {
            return rateLimitContext;
        }

        var moderatorBattleTag = TryGetModeratorBattleTag(context, logger);
        if (moderatorBattleTag != null)
        {
            rateLimitContext.HourlyLimit = ModeratorHourlyLimit;
            rateLimitContext.PolicyName = "replay-moderator";
            // Partition per moderator rather than per IP so colleagues behind one
            // address do not consume each other's budget.
            rateLimitContext.PartitionKey = $"moderator:{moderatorBattleTag}:{Scope}";
        }

        return rateLimitContext;
    }

    private static string TryGetModeratorBattleTag(ActionExecutingContext context, ILogger logger)
    {
        try
        {
            string authHeader = context.HttpContext.Request.Headers["Authorization"];
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                return null;
            }

            var token = authHeader["Bearer ".Length..].Trim();
            var authService = context.HttpContext.RequestServices.GetRequiredService<IW3CAuthenticationService>();
            var user = authService.GetUserByToken(token, true);

            if (user == null || string.IsNullOrEmpty(user.BattleTag)) return null;
            if (!user.IsAdmin) return null;
            if (user.Permissions == null || !user.Permissions.Contains(EPermission.Moderation)) return null;

            return user.BattleTag;
        }
        catch (Exception ex)
        {
            // A bad token must not break the request; fall through to the IP-based limit.
            logger.LogDebug(ex, "Could not resolve a moderator identity for replay rate limiting");
            return null;
        }
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
