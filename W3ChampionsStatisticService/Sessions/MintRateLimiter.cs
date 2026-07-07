using System;
using System.Collections.Generic;

namespace W3ChampionsStatisticService.Sessions;

/// <summary>
/// Generic keyed fixed-window rate limiter, in-memory and single-instance by design — mirrors
/// TicketStore's node-local placement. Key-agnostic: the caller composes keys with a prefix
/// discipline (e.g. "bt:{battleTag}") so one instance can serve multiple independent limits without
/// their windows colliding. Concurrency idiom mirrors Hubs/ConnectionMapping.cs and
/// Sessions/TicketStore.cs: a private Dictionary guarded by a single lock object, with every public
/// method doing its work inside that lock. Mirrors chat-service's Sessions/MintRateLimiter.cs.
/// </summary>
public class MintRateLimiter
{
    private readonly Dictionary<string, (DateTime WindowStart, int Count)> _windows =
        new Dictionary<string, (DateTime WindowStart, int Count)>();

    private readonly object _lock = new object();

    // Purge test seam — internals visible to WC3ChampionsStatisticService.Tests (see .csproj InternalsVisibleTo).
    internal int Count
    {
        get
        {
            lock (_lock)
            {
                return _windows.Count;
            }
        }
    }

    /// <summary>
    /// Fixed-window acquire for <paramref name="key"/>: allows up to <paramref name="limit"/> calls
    /// within a live SessionLimits.TicketMintWindow, then denies until the window rolls over. Purges
    /// stale windows (across all keys) opportunistically on every call so keys can't grow unbounded.
    /// </summary>
    public bool TryAcquire(string key, int limit, DateTime now)
    {
        lock (_lock)
        {
            PurgeStaleNoLock(now);

            if (_windows.TryGetValue(key, out var window) && window.WindowStart + SessionLimits.TicketMintWindow > now)
            {
                if (window.Count >= limit)
                {
                    return false;
                }

                _windows[key] = (window.WindowStart, window.Count + 1);
                return true;
            }

            _windows[key] = (now, 1);
            return true;
        }
    }

    // Caller must already hold _lock.
    private void PurgeStaleNoLock(DateTime now)
    {
        var staleKeys = new List<string>();
        foreach (var kvp in _windows)
        {
            if (kvp.Value.WindowStart + SessionLimits.TicketMintWindow <= now)
            {
                staleKeys.Add(kvp.Key);
            }
        }

        foreach (var staleKey in staleKeys)
        {
            _windows.Remove(staleKey);
        }
    }
}
