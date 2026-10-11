using System;

namespace W3ChampionsStatisticService.Sessions;

/// <summary>
/// wb-local constants for the auth ticket-mint flow (WB-1). Hard-coded by design — mirrors
/// chat-service's <c>ChatLimits</c> ticket values (TTL 60s, 10 mints/min per battleTag) so the two
/// services present a symmetric mint contract to the launcher. There is no per-IP mint limit here
/// (see <c>AuthSessionController</c> for the rationale).
/// </summary>
public static class SessionLimits
{
    /// <summary>Auth ticket TTL in seconds, counted from mint and never extended. Surfaced to the client
    /// as ExpiresInSeconds.</summary>
    public const int TicketTtlSeconds = 60;

    /// <summary>Auth ticket TTL. Derived from <see cref="TicketTtlSeconds"/> so the two never drift.</summary>
    public static readonly TimeSpan TicketTtl = TimeSpan.FromSeconds(TicketTtlSeconds);

    /// <summary>Most presentations a ticket accepts inside its TTL, failed ones included (see
    /// <see cref="TicketStore"/>). Lets the launcher's reconnect loop (first retry within 10s, then
    /// 2.5-7.5s, 5-15s, 10-30s) reuse one ticket for about a minute instead of minting per attempt.
    /// Surfaced to the client as MaxRedemptions, which is how a launcher knows this server allows
    /// reuse. Matches chat-service's ChatLimits.TicketMaxRedemptions.</summary>
    public const int TicketMaxRedemptions = 5;

    /// <summary>Ticket mint rate limit: fixed window per validated battleTag. 10/min tolerates
    /// reconnect flapping (matches chat-service's per-battleTag value).</summary>
    public const int TicketMintPerBattleTagLimit = 10;

    /// <summary>Fixed window for the per-battleTag mint limiter.</summary>
    public static readonly TimeSpan TicketMintWindow = TimeSpan.FromMinutes(1);
}
