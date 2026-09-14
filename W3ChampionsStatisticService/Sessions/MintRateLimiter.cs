using System;
using System.Collections.Generic;

namespace W3ChampionsStatisticService.Sessions;

/// <summary>
/// Generic keyed fixed-window rate limiter, in-memory and single-instance by design — mirrors
/// TicketStore's node-local placement. Key-agnostic: the caller composes keys with a prefix
/// discipline (e.g. "bt:{battleTag}") so one instance can serve multiple independent limits without
/// their windows colliding. Concurrency idiom mirrors Hubs/ConnectionMapping.cs and
/// Sessions/TicketStore.cs: a private Dictionary guarded by a single lock object, with every public
/// method doing its work inside that lock. Mirrors chat-service's Sessions/MintRateLimiter.cs, plus a
/// window length stored per key so the temporary-map quotas (one hour, one minute) can share it.
/// </summary>
public class MintRateLimiter
{
    private readonly Dictionary<string, (DateTime WindowStart, int Count, TimeSpan Window)> _windows =
        new Dictionary<string, (DateTime WindowStart, int Count, TimeSpan Window)>();

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
    /// Fixed-window acquire for <paramref name="key"/> over the default
    /// SessionLimits.TicketMintWindow. Kept as the ticket-mint call shape.
    /// </summary>
    public bool TryAcquire(string key, int limit, DateTime now)
        => TryAcquire(key, limit, now, SessionLimits.TicketMintWindow, out _);

    /// <summary>
    /// Fixed-window acquire for <paramref name="key"/>: allows up to <paramref name="limit"/> calls
    /// within <paramref name="window"/>, then denies until the window rolls over. The window length
    /// is stored PER KEY at the moment a window opens, so buckets of different lengths (one-minute
    /// pre-checks, one-hour uploads, one-minute ticket mints) share one instance without one caller's
    /// window length silently redefining another's. A live window keeps the length it opened with even
    /// if a later call passes a different one — it is neither shortened nor extended — and the next
    /// window takes the length passed when it opens. Purges stale windows (across all keys, each
    /// against its own length) opportunistically on every call so keys can't grow unbounded.
    /// </summary>
    /// <param name="window">Must be positive: a window that is already over would disable the limit.</param>
    /// <param name="retryAfter">
    /// Time left in the live window when the call is denied (always positive); <see cref="TimeSpan.Zero"/>
    /// otherwise.
    /// </param>
    public bool TryAcquire(string key, int limit, DateTime now, TimeSpan window, out TimeSpan retryAfter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);

        lock (_lock)
        {
            PurgeStaleNoLock(now);
            retryAfter = TimeSpan.Zero;

            if (_windows.TryGetValue(key, out var live) && live.WindowStart + live.Window > now)
            {
                if (live.Count >= limit)
                {
                    retryAfter = live.WindowStart + live.Window - now;
                    return false;
                }

                _windows[key] = (live.WindowStart, live.Count + 1, live.Window);
                return true;
            }

            _windows[key] = (now, 1, window);
            return true;
        }
    }

    // Caller must already hold _lock.
    private void PurgeStaleNoLock(DateTime now)
    {
        var staleKeys = new List<string>();
        foreach (var kvp in _windows)
        {
            if (kvp.Value.WindowStart + kvp.Value.Window <= now)
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
