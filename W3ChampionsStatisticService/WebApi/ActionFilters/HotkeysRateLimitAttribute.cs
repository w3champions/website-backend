using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Filters;
using W3ChampionsStatisticService.RateLimiting.Services;

namespace W3ChampionsStatisticService.WebApi.ActionFilters;

/// <summary>
/// Per-battleTag limit for hotkey uploads. Runs after the bearer filter (Order 0), which rewrites
/// the battleTag action argument to the authenticated user's canonical tag.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class HotkeysRateLimitAttribute : RateLimitAttribute, IOrderedFilter
{
    public HotkeysRateLimitAttribute()
    {
        Scope = "hotkeys";
        HourlyLimit = 30;
        DailyLimit = 200;
    }

    public int Order { get; set; } = 100;

    protected override Task<RateLimitContext> DetermineRateLimitContext(
        ActionExecutingContext context,
        IRateLimitService rateLimitService)
    {
        var battleTag = (string)context.ActionArguments["battleTag"];
        return Task.FromResult(new RateLimitContext
        {
            PolicyName = PolicyName,
            HourlyLimit = HourlyLimit,
            DailyLimit = DailyLimit,
            PartitionKey = $"user:{battleTag.ToLowerInvariant()}:{Scope}",
        });
    }
}
