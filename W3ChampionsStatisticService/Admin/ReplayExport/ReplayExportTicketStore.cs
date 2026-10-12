using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using MongoDB.Bson;

namespace W3ChampionsStatisticService.Admin.ReplayExport;

public class ReplayExportTicket
{
    public List<ObjectId> MatchIds { get; init; } = [];
    public string RequestedBy { get; init; }
    public string Label { get; init; }
    public string Description { get; init; }
    public ReplayExportFilter Filter { get; init; }

    /// <summary>Set for a repeat download of a download in the history; null for a new download.</summary>
    public string ExportId { get; init; }

    /// <summary>The controller that gave the ticket ("admin" or "store"). Only its download route takes it.</summary>
    public string Scope { get; init; }
}

/// <summary>
/// Short-lived, single-use download tickets. The browser downloads the ZIP with a plain GET (so it streams
/// to disk and shows native progress), and a plain GET cannot carry the bearer token. The admin gets a
/// ticket with an authenticated POST, and the GET presents the ticket instead.
///
/// Tickets live in memory: they only need to last until the browser starts the download, on the instance that
/// issued them.
/// </summary>
public class ReplayExportTicketStore(IMemoryCache cache)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly IMemoryCache _cache = cache;
    private readonly object _takeLock = new();

    /// <summary>
    /// A new ticket. It replaces the user's last ticket that is not used yet: a ticket keeps up to 5,000 game
    /// ids, and a user who asks again and again must not fill the memory.
    /// </summary>
    public (string Ticket, DateTimeOffset ExpiresAt) Create(ReplayExportTicket ticket)
    {
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var expiresAt = DateTimeOffset.UtcNow.Add(Lifetime);
        var user = UserKey(ticket.RequestedBy);
        lock (_takeLock)
        {
            if (_cache.TryGetValue(user, out string last)) _cache.Remove(Key(last));
            _cache.Set(Key(id), ticket, expiresAt);
            _cache.Set(user, id, expiresAt);
        }
        return (id, expiresAt);
    }

    /// <summary>The ticket, or null if it does not exist or has expired. The ticket stays in the store.</summary>
    public ReplayExportTicket Peek(string id) =>
        !string.IsNullOrEmpty(id) && _cache.TryGetValue(Key(id), out ReplayExportTicket ticket) ? ticket : null;

    /// <summary>Returns the ticket and removes it, or null if it does not exist or has expired.</summary>
    public ReplayExportTicket Take(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        // Two requests with the same ticket at the same time: only one of them gets it.
        lock (_takeLock)
        {
            if (!_cache.TryGetValue(Key(id), out ReplayExportTicket ticket)) return null;
            _cache.Remove(Key(id));
            return ticket;
        }
    }

    private static string Key(string id) => $"replay-export-ticket:{id}";

    private static string UserKey(string battleTag) => $"replay-export-ticket-of:{battleTag}";
}
