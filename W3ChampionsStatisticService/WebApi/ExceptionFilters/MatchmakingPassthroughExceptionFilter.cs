using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.WebUtilities;
using W3C.Domain.MatchmakingService;

namespace W3ChampionsStatisticService.WebApi.ExceptionFilters;

/// <summary>
/// Answers a MatchmakingPassthroughException with matchmaking's status code. A 4xx JSON object body is returned verbatim
/// (error, code, field, rule, data, or the requireAdmin errors[] shape). A 5xx gets a generic INTERNAL body so upstream
/// internals (hosts, database text) never reach the browser.
/// </summary>
public sealed class MatchmakingPassthroughExceptionFilter : ExceptionFilterAttribute
{
    internal const string InternalErrorBody = "{\"error\":\"Matchmaking service error\",\"code\":\"INTERNAL\"}";

    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is not MatchmakingPassthroughException passthrough) return;

        var status = (int)passthrough.StatusCode;
        context.Result = new ContentResult
        {
            StatusCode = status,
            ContentType = "application/json",
            Content = status >= 500 ? InternalErrorBody : ClientErrorBody(status, passthrough.Body),
        };
        context.ExceptionHandled = true;
    }

    private static string ClientErrorBody(int status, string body) =>
        IsJsonObject(body) ? body : JsonSerializer.Serialize(new { error = ReasonPhrases.GetReasonPhrase(status) });

    private static bool IsJsonObject(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return false;
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
