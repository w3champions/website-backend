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
    /// <para>
    /// Every comparison subtracts two dates (a difference that always fits in a TimeSpan) instead of
    /// adding the window to a date, so no stored window — up to TimeSpan.MaxValue — can make this or any
    /// later call throw.
    /// </para>
    /// </summary>
    /// <param name="limit">Must be positive: a new window always grants its first call.</param>
    /// <param name="window">Must be positive: a window that is already over would disable the limit.</param>
    /// <param name="retryAfter">
    /// Time left in the live window when the call is denied (always positive, saturating at
    /// <see cref="TimeSpan.MaxValue"/>); <see cref="TimeSpan.Zero"/> otherwise.
    /// </param>
    public bool TryAcquire(string key, int limit, DateTime now, TimeSpan window, out TimeSpan retryAfter)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);

        lock (_lock)
        {
            PurgeStaleNoLock(now);
            retryAfter = TimeSpan.Zero;

            if (_windows.TryGetValue(key, out var live) && now - live.WindowStart < live.Window)
            {
                if (live.Count >= limit)
                {
                    retryAfter = Remaining(live.Window, now - live.WindowStart);
                    return false;
                }

                _windows[key] = (live.WindowStart, live.Count + 1, live.Window);
                return true;
            }

            _windows[key] = (now, 1, window);
            return true;
        }
    }

    // window - elapsed, saturating: elapsed is negative only when a caller passes a `now` older than the
    // window start, and adding that back to a near-TimeSpan.MaxValue window would overflow.
    private static TimeSpan Remaining(TimeSpan window, TimeSpan elapsed)
        => elapsed < TimeSpan.Zero && window > TimeSpan.MaxValue + elapsed ? TimeSpan.MaxValue : window - elapsed;

    // Caller must already hold _lock.
    private void PurgeStaleNoLock(DateTime now)
    {
        var staleKeys = new List<string>();
        foreach (var kvp in _windows)
        {
            if (now - kvp.Value.WindowStart >= kvp.Value.Window)
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
