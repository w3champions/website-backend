using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.LagReports;

/// <summary>The lag-report persistence the relay telemetry needs.</summary>
public interface ILagReportRelayStore
{
    /// <summary>The report with only its id and each player's FloPlayerId and RelayChain loaded.</summary>
    Task<LagReport> GetForRelayTelemetry(int floGameId);

    /// <summary>Replaces the chain on every entry with that flo player id and battletag, and on no other.</summary>
    Task UpdatePlayerRelayChain(string reportId, int floPlayerId, string battleTag, PlayerRelayChain chain);
}

/// <summary>
/// Fetches each reporting player's per-hop relay chain from the flo controller and stores it
/// on the lag report. Every entry point is safe to fire and forget: failures are logged and
/// leave the stored chain as it was, so the next trigger retries.
/// </summary>
public interface IRelayTelemetryService
{
    /// <summary>On a player's submit: fetch that player's chain unless the stored one is final.</summary>
    Task FetchForPlayer(int floGameId, int floPlayerId);

    /// <summary>At match end: refresh every player whose chain is missing or still open.</summary>
    Task RefreshOpen(int floGameId);
}

[Trace]
public class RelayTelemetryService(IFloControllerRelayClient client, ILagReportRelayStore store, IFloStatsService floStats) : IRelayTelemetryService
{
    // Coalesces a submit and a match-end refresh racing for the same player, so they cannot
    // overwrite each other's merge. Lazy because GetOrAdd may run its factory more than once
    // under contention; only the stored Lazy ever starts a fetch. Entries are removed on completion.
    private readonly ConcurrentDictionary<(int GameId, int PlayerId), Lazy<Task>> _inflight = new();

    public Task FetchForPlayer(int floGameId, int floPlayerId) => FetchCoalesced(floGameId, floPlayerId, roster: null);

    private async Task FetchCoalesced(int floGameId, int floPlayerId, Dictionary<int, string> roster)
    {
        var key = (floGameId, floPlayerId);
        var lazy = _inflight.GetOrAdd(key, k => new Lazy<Task>(() => FetchAndStore(k.GameId, k.PlayerId, roster)));
        try
        {
            await lazy.Value;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "RelayTelemetry: fetch for game {GameId} player {PlayerId} failed", floGameId, floPlayerId);
        }
        finally
        {
            _inflight.TryRemove(new KeyValuePair<(int, int), Lazy<Task>>(key, lazy));
        }
    }

    public async Task RefreshOpen(int floGameId)
    {
        List<int> playerIds;
        Dictionary<int, string> roster;
        try
        {
            var report = await store.GetForRelayTelemetry(floGameId);
            if (report == null) return;

            playerIds = report.Players
                .Where(p => p.FloPlayerId.HasValue)
                .GroupBy(p => p.FloPlayerId.Value)
                .Where(g => RelayChainMerge.NeedsRefresh(CurrentChain(g)))
                .Select(g => g.Key)
                .ToList();
            if (playerIds.Count == 0) return;

            roster = await floStats.FetchGamePlayers(floGameId);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "RelayTelemetry: loading report for game {GameId} failed", floGameId);
            return;
        }

        // Sequential: the controller runs only a few walks at once and rejects the rest.
        foreach (var playerId in playerIds)
        {
            await FetchCoalesced(floGameId, playerId, roster);
        }
    }

    private async Task FetchAndStore(int floGameId, int floPlayerId, Dictionary<int, string> roster)
    {
        var report = await store.GetForRelayTelemetry(floGameId);
        var claimed = report?.Players.Where(p => p.FloPlayerId == floPlayerId).ToList();
        if (claimed == null || claimed.Count == 0) return;

        var existing = CurrentChain(claimed);
        var needsFetch = RelayChainMerge.NeedsRefresh(existing);
        // A re-submission after the chain was final adds an entry without it.
        if (!needsFetch && claimed.All(p => p.RelayChain != null)) return;

        // FloPlayerId comes from the launcher; only flo's own roster says whose id it is.
        roster ??= await floStats.FetchGamePlayers(floGameId);
        if (roster == null)
        {
            Log.Information("RelayTelemetry: no flo roster for game {GameId}; player {PlayerId} retries on the next trigger", floGameId, floPlayerId);
            return;
        }
        var entries = roster.TryGetValue(floPlayerId, out var owner)
            ? claimed.Where(p => string.Equals(p.BattleTag, owner, StringComparison.OrdinalIgnoreCase)).ToList()
            : [];
        if (entries.Count == 0)
        {
            Log.Warning("RelayTelemetry: game {GameId} flo player {PlayerId} is {Owner}, not the reporter {Claimants}; ignoring",
                floGameId, floPlayerId, owner, claimed.Select(p => p.BattleTag).Distinct());
            return;
        }

        existing = CurrentChain(entries);
        if (!RelayChainMerge.NeedsRefresh(existing))
        {
            if (entries.Any(p => p.RelayChain == null))
            {
                await store.UpdatePlayerRelayChain(report.Id, floPlayerId, entries[0].BattleTag, existing);
            }
            return;
        }

        var reply = await client.GetGameRelayTelemetry(floGameId, floPlayerId);
        if (reply == null) return;
        if (reply.Connections.Count == 0)
        {
            // Nothing to add; writing would only blur "not fetched yet" with "fetched, empty".
            Log.Information("RelayTelemetry: controller returned no connections for game {GameId} player {PlayerId}", floGameId, floPlayerId);
            return;
        }

        var merged = RelayChainMerge.Merge(existing, RelayChainMapper.FromReply(reply, DateTime.UtcNow));
        await store.UpdatePlayerRelayChain(report.Id, floPlayerId, entries[0].BattleTag, merged);
    }

    // A player who submitted twice has two entries; every update writes all of them.
    private static PlayerRelayChain CurrentChain(IEnumerable<LagReportPlayer> entries) =>
        entries.Select(p => p.RelayChain).FirstOrDefault(c => c != null);
}
