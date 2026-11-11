using Microsoft.AspNetCore.Mvc;

namespace W3ChampionsStatisticService.Admin;

/// <summary>Result helpers shared by the smurf moderation proxy controllers (hardware and events).</summary>
internal static class SmurfModerationResults
{
    /// <summary>Matchmaking's status code and JSON body, unchanged.</summary>
    public static IActionResult Forward(MatchmakingProxyResult result) =>
        new ContentResult { StatusCode = result.StatusCode, ContentType = "application/json", Content = result.Body };

    public static IActionResult Invalid(string field, string message) =>
        new BadRequestObjectResult(new { error = $"{field}: {message}", code = "INVALID_REQUEST", field });

    public static IActionResult InvalidLimit(int? limit, int maxLimit) =>
        limit is null || (limit >= 1 && limit <= maxLimit) ? null : Invalid("limit", $"must be between 1 and {maxLimit}");
}
