using System;
using System.Threading.Tasks;
using Serilog;

namespace W3ChampionsStatisticService.Services;

/// <summary>
/// Admin BattleTags from identification-service, cached for <see cref="Ttl"/>. Deliberately not an
/// IMemoryCache entry: expiry would drop the last good set, which must be kept when a refresh fails.
/// <para>
/// Stale-while-revalidate: identification-service is never awaited on the request path once a set
/// has loaded. An expired set is served as-is while at most one background refresh runs; a failed
/// refresh keeps the last good set and is retried after <see cref="FailureRetryDelay"/>.
/// </para>
/// <para>
/// Never loaded (startup, or identification-service down since startup): callers wait for the
/// in-flight first load for at most <see cref="FirstLoadWait"/>, then fail open (nobody is an
/// admin) while the load continues in the background. This keeps chat-service's
/// chat-relationships call (2s timeout) well within budget.
/// </para>
/// </summary>
public sealed class AdminRoster(IdentityServiceClient identityClient, TimeProvider time) : IAdminRoster
{
    internal static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan FailureRetryDelay = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan FirstLoadWait = TimeSpan.FromMilliseconds(500);

    private readonly object _refreshGate = new();
    private Task _refresh = Task.CompletedTask; // guarded by _refreshGate
    private int _consecutiveFailures; // only touched by the single in-flight refresh
    private volatile Snapshot _snapshot = new(AdminSnapshot.Empty, DateTimeOffset.MinValue, Loaded: false);

    /// <summary>Test seam: the current (or last) background refresh. Never faults.</summary>
    internal Task PendingRefresh
    {
        get { lock (_refreshGate) return _refresh; }
    }

    public async Task<AdminSnapshot> GetSnapshotAsync()
    {
        var snapshot = _snapshot;
        if (time.GetUtcNow() < snapshot.NextRefreshAt)
        {
            return snapshot.Admins;
        }

        var refresh = StartRefreshIfIdle();
        if (snapshot.Loaded)
        {
            return snapshot.Admins;
        }

        try
        {
            await refresh.WaitAsync(FirstLoadWait, time);
        }
        catch (TimeoutException)
        {
            // Fail open below; the load keeps running and is picked up once it lands.
        }

        return _snapshot.Admins;
    }

    private Task StartRefreshIfIdle()
    {
        lock (_refreshGate)
        {
            // Re-check under the lock: a refresh may have completed since the caller read the snapshot.
            if (!_refresh.IsCompleted || time.GetUtcNow() < _snapshot.NextRefreshAt)
            {
                return _refresh;
            }

            _refresh = Task.Run(Refresh);
            return _refresh;
        }
    }

    /// <summary>Catches everything, so the background task never faults (no unobserved exceptions).</summary>
    private async Task Refresh()
    {
        var current = _snapshot;
        try
        {
            var tags = await identityClient.GetAdminBattleTags();
            _snapshot = new Snapshot(new AdminSnapshot(tags ?? []), time.GetUtcNow() + Ttl, Loaded: true);
            _consecutiveFailures = 0;
        }
        catch (Exception ex)
        {
            // An outage would otherwise log a warning every FailureRetryDelay: warn once, then debug.
            var level = _consecutiveFailures++ == 0 ? Serilog.Events.LogEventLevel.Warning : Serilog.Events.LogEventLevel.Debug;
            Log.Write(level, ex, "Admin roster refresh failed ({Failures} in a row); serving {Count} cached admins",
                _consecutiveFailures, current.Admins.Tags.Count);
            _snapshot = current with { NextRefreshAt = time.GetUtcNow() + FailureRetryDelay };
        }
    }

    private sealed record Snapshot(AdminSnapshot Admins, DateTimeOffset NextRefreshAt, bool Loaded);
}
