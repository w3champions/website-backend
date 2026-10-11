using System;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.Sessions;

public interface ITicketStore
{
    /// <summary>Mints a ticket bound to the validated identity snapshot. Purges expired tickets.</summary>
    string Mint(W3CUserAuthenticationDto identity, DateTime now);

    /// <summary>
    /// Redeems a ticket for one hub connection. Succeeds only within <see cref="SessionLimits.TicketTtl"/>
    /// of mint (absolute, never extended), for at most <see cref="SessionLimits.TicketMaxRedemptions"/>
    /// presentations, and only while no other connection holds the ticket. A presentation while the
    /// ticket is held revokes it.
    /// </summary>
    bool TryRedeem(string ticket, string connectionId, DateTime now, out W3CUserAuthenticationDto identity);

    /// <summary>Frees the ticket <paramref name="connectionId"/> holds, if any, once that connection has
    /// closed, so a reconnect can redeem it again.</summary>
    void Release(string connectionId);
}
