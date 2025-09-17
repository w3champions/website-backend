using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using W3ChampionsStatisticService.RateLimiting.Services;

namespace W3ChampionsStatisticService.WebApi.ActionFilters;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class RateLimitAttribute : Attribute, IAsyncActionFilter
{
    /// <summary>
    /// The scope/category for this rate limit (e.g., "replay", "stats", "profile")
    /// </summary>
    public string Scope { get; set; } = "default";

    /// <summary>
    /// Hourly rate limit
    /// </summary>
    public int HourlyLimit { get; set; } = 100;

    /// <summary>
    /// Daily rate limit
    /// </summary>
    public int DailyLimit { get; set; } = 1000;

    /// <summary>
    /// The policy name for this rate limit
    /// </summary>
    public string PolicyName { get; set; } = "default";

    public virtual async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var rateLimitService = context.HttpContext.RequestServices.GetRequiredService<IRateLimitService>();
        var bucketService = context.HttpContext.RequestServices.GetRequiredService<IRateLimitBucketService>();
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<RateLimitAttribute>>();

        // Get the rate limit context (checks for API tokens, gets IP, etc.)
        var rateLimitContext = await DetermineRateLimitContext(context, rateLimitService);

        // Store the context in HttpContext items
        context.HttpContext.Items["RateLimitContext"] = rateLimitContext;

        logger.LogDebug("Rate limit check - Policy: {Policy}, Hourly: {Hourly}, Daily: {Daily}, Partition: {Partition}",
            rateLimitContext.PolicyName,
            rateLimitContext.HourlyLimit,
            rateLimitContext.DailyLimit,
            rateLimitContext.PartitionKey);

        // Try to acquire a lease
        var lease = await bucketService.TryAcquireAsync(
            rateLimitContext.PartitionKey,
            rateLimitContext.HourlyLimit,
            rateLimitContext.DailyLimit);

        if (!lease.IsAcquired)
        {
            logger.LogWarning("Rate limit exceeded for partition {Partition} with policy {Policy}",
                rateLimitContext.PartitionKey, rateLimitContext.PolicyName);

            context.Result = new StatusCodeResult(StatusCodes.Status429TooManyRequests);
            return;
        }

        // Lease acquired, continue
        try
        {
            await next.Invoke();
        }
        finally
        {
            lease.Dispose();
        }
    }

    protected virtual async Task<RateLimitContext> DetermineRateLimitContext(
        ActionExecutingContext context,
        IRateLimitService rateLimitService)
    {
        return await rateLimitService.DetermineRateLimitContext(
            context.HttpContext,
            Scope,
            PolicyName,
            HourlyLimit,
            DailyLimit);
    }
}
