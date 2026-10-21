using System.Text.Json;

namespace W3ChampionsStatisticService.Admin;

/// <summary>
/// A matchmaking admin response forwarded as is (status code and raw JSON body). Matchmaking answering 403, which
/// means the shared admin secret is not accepted, and an unreachable matchmaking both become a generic 502 so
/// neither the secret nor the upstream address reaches the browser.
/// </summary>
public sealed record MatchmakingProxyResult(int StatusCode, string Body)
{
    public const string UnavailableMessage = "Matchmaking service unavailable";

    public static MatchmakingProxyResult Unavailable() =>
        new(502, JsonSerializer.Serialize(new { error = UnavailableMessage }));
}
