using System.Net;

namespace W3C.Contracts.Matchmaking;

public class ErrorResponse
{
    public MMError[] Errors { get; set; } = new MMError[0];

    // Some matchmaking-service endpoints (e.g. commercial-license) answer { "error": "<message>" } instead of { "errors": [...] }.
    public string Error { get; set; }

    /// <summary>
    /// Single rule for which upstream matchmaking error text may reach callers: only 4xx messages, for both the
    /// { "error" } and the legacy { "errors": [...] } shape. Matchmaking answers 5xx with raw err.message
    /// (internal hosts, database text) and uses errors[] on 5xx only for the "Admin API is not configured"
    /// guard in shared/helpers/api.helper.ts, never for validation, so 5xx bodies are never forwarded.
    /// Returns null when the framework default message should be used.
    /// </summary>
    public static string ClientVisibleMessage(HttpStatusCode statusCode, string upstreamMessage) =>
        (int)statusCode < 500 && !string.IsNullOrEmpty(upstreamMessage) ? upstreamMessage : null;
}
