using System;
using System.Net;

namespace W3C.Domain.MatchmakingService;

/// <summary>
/// A failed call to the matchmaking commercial-events admin API, carried unchanged to MatchmakingPassthroughExceptionFilter
/// so the website receives matchmaking's error body (error, code, field, rule, data). The other client methods keep
/// HandleMMError and HttpRequestException.
/// </summary>
public sealed class MatchmakingPassthroughException(HttpStatusCode statusCode, string body, Exception innerException = null)
    : Exception($"Matchmaking commercial-events request failed with status {(int)statusCode}.", innerException)
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    /// <summary>The raw response body; null when matchmaking could not be reached.</summary>
    public string Body { get; } = body;
}
