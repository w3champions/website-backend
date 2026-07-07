using System;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.Sessions;

public interface ITicketStore
{
    /// <summary>Mints a one-time ticket bound to the validated identity snapshot. Purges expired tickets.</summary>
    string Mint(W3CUserAuthenticationDto identity, DateTime now);

    /// <summary>Consumes atomically: true exactly once per ticket, and only within SessionLimits.TicketTtl of mint.</summary>
    bool TryConsume(string ticket, DateTime now, out W3CUserAuthenticationDto identity);
}
