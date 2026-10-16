using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace W3ChampionsStatisticService.Services;

/// <summary>
/// Admin BattleTags from identification-service, cached for <see cref="Ttl"/>. Deliberately not an
/// IMemoryCache entry: expiry would drop the last good set, which must be kept when a refresh fails.
/// Fails open: if the roster was never loaded, nobody is an admin.
/// </summary>
public sealed class AdminRoster(IdentityServiceClient identityClient, TimeProvider time) : IAdminRoster
{
    internal static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan FailureRetryDelay = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private volatile Snapshot _snapshot = new(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        DateTimeOffset.MinValue);

    public async Task<IReadOnlySet<string>> GetAdminBattleTags()
    {
        var snapshot = _snapshot;
        if (time.GetUtcNow() < snapshot.NextRefreshAt)
        {
            return snapshot.Tags;
        }

        await _refreshLock.WaitAsync();
        try
        {
            // Re-check: another caller may have refreshed while we waited.
            snapshot = _snapshot;
            if (time.GetUtcNow() < snapshot.NextRefreshAt)
            {
                return snapshot.Tags;
            }

            _snapshot = snapshot = await Refresh(snapshot);
            return snapshot.Tags;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async Task<bool> IsAdmin(string battleTag)
    {
        if (string.IsNullOrWhiteSpace(battleTag))
        {
            return false;
        }

        return (await GetAdminBattleTags()).Contains(battleTag);
    }

    private async Task<Snapshot> Refresh(Snapshot current)
    {
        try
        {
            var tags = await identityClient.GetAdminBattleTags();
            var set = new HashSet<string>(
                tags.Where(t => !string.IsNullOrWhiteSpace(t)),
                StringComparer.OrdinalIgnoreCase);
            return new Snapshot(set, time.GetUtcNow() + Ttl);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Admin roster refresh failed; serving {Count} cached admins", current.Tags.Count);
            return current with { NextRefreshAt = time.GetUtcNow() + FailureRetryDelay };
        }
    }

    private sealed record Snapshot(IReadOnlySet<string> Tags, DateTimeOffset NextRefreshAt);
}
