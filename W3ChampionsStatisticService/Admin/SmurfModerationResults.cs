using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace W3ChampionsStatisticService.Admin;

/// <summary>Result helpers shared by the smurf moderation proxy controllers (hardware and events).</summary>
internal static class SmurfModerationResults
{
    /// <summary>Matchmaking's status code and JSON body, unchanged, plus its Retry-After header when it sent a valid one.</summary>
    public static IActionResult Forward(MatchmakingProxyResult result) =>
        new ForwardedResult { StatusCode = result.StatusCode, ContentType = "application/json", Content = result.Body, RetryAfter = result.RetryAfter };

    public static IActionResult Invalid(string field, string message) =>
        new BadRequestObjectResult(new { error = $"{field}: {message}", code = "INVALID_REQUEST", field });

    public static IActionResult InvalidLimit(int? limit, int maxLimit) =>
        limit is null || (limit >= 1 && limit <= maxLimit) ? null : Invalid("limit", $"must be between 1 and {maxLimit}");
}

/// <summary>A JSON content result that also carries the upstream Retry-After header.</summary>
internal sealed class ForwardedResult : ContentResult
{
    public string RetryAfter { get; init; }

    public override Task ExecuteResultAsync(ActionContext context)
    {
        if (RetryAfter != null) context.HttpContext.Response.Headers[HeaderNames.RetryAfter] = RetryAfter;
        return base.ExecuteResultAsync(context);
    }
}
