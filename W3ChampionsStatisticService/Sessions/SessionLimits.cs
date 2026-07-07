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
    /// <summary>Auth ticket TTL in seconds (one-time). Surfaced to the client as ExpiresInSeconds.</summary>
    public const int TicketTtlSeconds = 60;

    /// <summary>Auth ticket TTL (one-time). Derived from <see cref="TicketTtlSeconds"/> so the two never drift.</summary>
    public static readonly TimeSpan TicketTtl = TimeSpan.FromSeconds(TicketTtlSeconds);

    /// <summary>Ticket mint rate limit: fixed window per validated battleTag. 10/min tolerates
    /// reconnect flapping (matches chat-service's per-battleTag value).</summary>
    public const int TicketMintPerBattleTagLimit = 10;

    /// <summary>Fixed window for the per-battleTag mint limiter.</summary>
    public static readonly TimeSpan TicketMintWindow = TimeSpan.FromMinutes(1);
}
