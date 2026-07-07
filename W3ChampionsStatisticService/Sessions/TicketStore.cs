using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.Sessions;

/// <summary>
/// Single-instance in-memory ticket store, by design: tickets are short-lived (SessionLimits.TicketTtl)
/// and node-local, so they never need to survive a restart or be shared across website-backend
/// instances. Concurrency idiom mirrors Hubs/ConnectionMapping.cs: a private Dictionary guarded by a
/// single lock object, with every public method doing its work inside that lock. Mirrors
/// chat-service's Sessions/TicketStore.cs, holding wb's <see cref="W3CUserAuthenticationDto"/> identity.
/// </summary>
public class TicketStore : ITicketStore
{
    private readonly Dictionary<string, (W3CUserAuthenticationDto Identity, DateTime IssuedAt)> _tickets =
        new Dictionary<string, (W3CUserAuthenticationDto Identity, DateTime IssuedAt)>();

    private readonly object _lock = new object();

    // Purge test seam — internals visible to WC3ChampionsStatisticService.Tests (see .csproj InternalsVisibleTo).
    internal int Count
    {
        get
        {
            lock (_lock)
            {
                return _tickets.Count;
            }
        }
    }

    public string Mint(W3CUserAuthenticationDto identity, DateTime now)
    {
        lock (_lock)
        {
            PurgeExpiredNoLock(now);

            var ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            _tickets[ticket] = (identity, now);
            return ticket;
        }
    }

    public bool TryConsume(string ticket, DateTime now, out W3CUserAuthenticationDto identity)
    {
        lock (_lock)
        {
            // Burn on every hit — consuming, expired-or-not, always removes the ticket so it
            // can never be presented again.
            if (!_tickets.Remove(ticket, out var entry))
            {
                identity = null;
                return false;
            }

            if (now > entry.IssuedAt + SessionLimits.TicketTtl)
            {
                identity = null;
                return false;
            }

            identity = entry.Identity;
            return true;
        }
    }

    // Caller must already hold _lock.
    private void PurgeExpiredNoLock(DateTime now)
    {
        var expiredTickets = new List<string>();
        foreach (var kvp in _tickets)
        {
            if (kvp.Value.IssuedAt + SessionLimits.TicketTtl < now)
            {
                expiredTickets.Add(kvp.Key);
            }
        }

        foreach (var expiredTicket in expiredTickets)
        {
            _tickets.Remove(expiredTicket);
        }
    }
}
