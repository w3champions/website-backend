using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Serilog;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.Sessions;

/// <summary>
/// Single-instance in-memory ticket store, by design: tickets are short-lived (SessionLimits.TicketTtl)
/// and node-local, so they never need to survive a restart or be shared across website-backend
/// instances. Concurrency idiom mirrors Hubs/ConnectionMapping.cs: a private Dictionary guarded by a
/// single lock object, with every public method doing its work inside that lock. Mirrors
/// chat-service's Sessions/TicketStore.cs, holding wb's <see cref="W3CUserAuthenticationDto"/> identity.
///
/// <para>Redemption rules. A ticket lets the launcher reconnect several times inside its TTL without
/// minting again, because it cannot tell whether a failed attempt reached the hub. It still never
/// behaves like a long-lived bearer:</para>
/// <list type="bullet">
/// <item>The TTL runs from mint and is never extended by a redemption.</item>
/// <item>Each presentation counts. After <see cref="SessionLimits.TicketMaxRedemptions"/>
/// presentations the ticket is gone.</item>
/// <item>At most one connection holds a ticket. Presenting a ticket while a connection holds it is
/// treated as a replay: the presentation fails and the ticket is revoked. The holder keeps its
/// connection.</item>
/// <item>An unknown, expired, revoked or exhausted ticket fails the same way, so a client can't
/// tell the cases apart.</item>
/// </list>
/// <para>Unlike a single-use ticket, a released ticket is still live until its TTL ends, so a copy
/// of the hub URL is worth something for that long. That is acceptable only while nothing logs the
/// hub URL's query string: keep proxy access logs off (or query-free) for the hub paths.</para>
/// </summary>
public class TicketStore : ITicketStore
{
    private sealed class Entry(W3CUserAuthenticationDto identity, DateTime issuedAt)
    {
        public W3CUserAuthenticationDto Identity { get; } = identity;
        public DateTime IssuedAt { get; } = issuedAt;
        public int Presentations { get; set; }
        public string HolderConnectionId { get; set; }
    }

    private readonly Dictionary<string, Entry> _tickets = new Dictionary<string, Entry>();

    // Holding connection id → ticket, so the hub can release by connection on disconnect.
    private readonly Dictionary<string, string> _ticketByHolder = new Dictionary<string, string>();

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
            _tickets[ticket] = new Entry(identity, now);
            return ticket;
        }
    }

    public bool TryRedeem(string ticket, string connectionId, DateTime now, out W3CUserAuthenticationDto identity)
    {
        identity = null;
        if (string.IsNullOrEmpty(ticket) || string.IsNullOrEmpty(connectionId))
        {
            return false;
        }

        lock (_lock)
        {
            if (!_tickets.TryGetValue(ticket, out var entry))
            {
                return false;
            }

            entry.Presentations += 1;

            var expired = now > entry.IssuedAt + SessionLimits.TicketTtl;
            var exhausted = entry.Presentations > SessionLimits.TicketMaxRedemptions;
            var held = entry.HolderConnectionId != null;
            if (expired || exhausted || held)
            {
                // Burn: an expired or exhausted ticket can never succeed again, and a presentation
                // while another connection holds the ticket is a replay, so revoke it.
                if (held && !expired && !exhausted)
                {
                    // Usually a reconnect racing the server noticing its old connection closed, but
                    // also what a stolen ticket looks like. Logged (never the ticket) so abuse shows up.
                    Log.Information("Ticket for {BattleTag} presented by {ConnectionId} while held by {HolderConnectionId}; revoked",
                        entry.Identity?.BattleTag, connectionId, entry.HolderConnectionId);
                }
                RemoveNoLock(ticket, entry);
                return false;
            }

            entry.HolderConnectionId = connectionId;
            _ticketByHolder[connectionId] = ticket;
            identity = entry.Identity;
            return true;
        }
    }

    public void Release(string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId))
        {
            return;
        }

        lock (_lock)
        {
            if (_ticketByHolder.Remove(connectionId, out var ticket)
                && _tickets.TryGetValue(ticket, out var entry)
                && entry.HolderConnectionId == connectionId)
            {
                entry.HolderConnectionId = null;
            }
        }
    }

    // Caller must already hold _lock.
    private void RemoveNoLock(string ticket, Entry entry)
    {
        _tickets.Remove(ticket);
        if (entry.HolderConnectionId != null
            && _ticketByHolder.TryGetValue(entry.HolderConnectionId, out var held)
            && held == ticket)
        {
            _ticketByHolder.Remove(entry.HolderConnectionId);
        }
    }

    // Caller must already hold _lock.
    private void PurgeExpiredNoLock(DateTime now)
    {
        var expiredTickets = new List<KeyValuePair<string, Entry>>();
        foreach (var kvp in _tickets)
        {
            if (kvp.Value.IssuedAt + SessionLimits.TicketTtl < now)
            {
                expiredTickets.Add(kvp);
            }
        }

        foreach (var expired in expiredTickets)
        {
            RemoveNoLock(expired.Key, expired.Value);
        }
    }
}
