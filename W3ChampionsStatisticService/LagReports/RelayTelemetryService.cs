using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.LagReports;

/// <summary>The lag-report persistence the relay telemetry needs.</summary>
public interface ILagReportRelayStore
{
    /// <summary>The report with only its id and each player's FloPlayerId and RelayChain loaded.</summary>
    Task<LagReport> GetForRelayTelemetry(int floGameId);

    /// <summary>Replaces the chain on every entry with that flo player id and one of these battletags, and on no other.</summary>
    Task UpdatePlayerRelayChain(string reportId, int floPlayerId, IReadOnlyCollection<string> battleTags, PlayerRelayChain chain);
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
    private readonly ConcurrentDictionary<(int GameId, int PlayerId), Lazy<Task<bool>>> _inflight = new();

    /// <summary>The controller runs at most 4 walks at once and rejects the rest; leave it one.</summary>
    public const int MaxConcurrentFetches = 3;

    private static readonly TimeSpan SlotWait = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _fetchSlots = new(MaxConcurrentFetches);

    /// <summary>
    /// A submit after the match-end event is the player's last trigger, and connections may still
    /// be closing at either trigger, so both retry a few times while the chain stays incomplete.
    /// </summary>
    internal TimeSpan[] LateRetryDelays { get; set; } = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(4)];

    /// <summary>The most recent background match-end retry; exposed for tests.</summary>
    internal Task PendingRefreshRetries { get; private set; } = Task.CompletedTask;

    public async Task FetchForPlayer(int floGameId, int floPlayerId)
    {
        var incomplete = await FetchCoalesced(floGameId, floPlayerId, roster: null);
        foreach (var delay in LateRetryDelays)
        {
            if (!incomplete) return;
            await Task.Delay(delay);
            incomplete = await FetchCoalesced(floGameId, floPlayerId, roster: null);
        }
    }

    // Returns true while the player's chain is still worth fetching again.
    private async Task<bool> FetchCoalesced(int floGameId, int floPlayerId, Dictionary<int, string> roster, bool mayJoin = true)
    {
        var key = (floGameId, floPlayerId);
        var mine = new Lazy<Task<bool>>(() => FetchAndStoreBounded(floGameId, floPlayerId, roster));
        var lazy = _inflight.GetOrAdd(key, mine);
        try
        {
            var incomplete = await lazy.Value;
            // The joined fetch may have read the report before this caller's entry was added.
            return ReferenceEquals(lazy, mine) || !mayJoin || incomplete
                ? incomplete
                : await FetchCoalesced(floGameId, floPlayerId, roster, mayJoin: false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "RelayTelemetry: fetch for game {GameId} player {PlayerId} failed", floGameId, floPlayerId);
            return true;
        }
        finally
        {
            _inflight.TryRemove(new KeyValuePair<(int, int), Lazy<Task<bool>>>(key, lazy));
        }
    }

    private async Task<bool> FetchAndStoreBounded(int floGameId, int floPlayerId, Dictionary<int, string> roster)
    {
        if (!await _fetchSlots.WaitAsync(SlotWait))
        {
            Log.Warning("RelayTelemetry: all {Slots} fetch slots busy for {Wait}; skipping game {GameId} player {PlayerId}",
                MaxConcurrentFetches, SlotWait, floGameId, floPlayerId);
            return true;
        }
        try
        {
            return await FetchAndStore(floGameId, floPlayerId, roster);
        }
        finally
        {
            _fetchSlots.Release();
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
            if (roster == null)
            {
                Log.Information("RelayTelemetry: no flo roster for game {GameId}; skipping the match-end refresh", floGameId);
                return;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "RelayTelemetry: loading report for game {GameId} failed", floGameId);
            return;
        }

        // Concurrent; _fetchSlots keeps the controller under its walk limit.
        var results = await Task.WhenAll(playerIds.Select(id => FetchCoalesced(floGameId, id, roster)));
        var incomplete = playerIds.Where((_, i) => results[i]).ToList();
        if (incomplete.Count > 0 && LateRetryDelays.Length > 0)
        {
            // Connections are often still closing when the match ends. Retry in the background
            // so the read-model handler is not held for minutes.
            PendingRefreshRetries = RetryIncomplete(floGameId, incomplete, roster);
        }
    }

    private async Task RetryIncomplete(int floGameId, List<int> playerIds, Dictionary<int, string> roster)
    {
        try
        {
            foreach (var delay in LateRetryDelays)
            {
                await Task.Delay(delay);
                var results = await Task.WhenAll(playerIds.Select(id => FetchCoalesced(floGameId, id, roster)));
                playerIds = playerIds.Where((_, i) => results[i]).ToList();
                if (playerIds.Count == 0) return;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "RelayTelemetry: match-end retries for game {GameId} failed", floGameId);
        }
    }

    private async Task<bool> FetchAndStore(int floGameId, int floPlayerId, Dictionary<int, string> roster)
    {
        var report = await store.GetForRelayTelemetry(floGameId);
        var claimed = report?.Players.Where(p => p.FloPlayerId == floPlayerId).ToList();
        if (claimed == null || claimed.Count == 0) return false;

        var existing = CurrentChain(claimed);
        var needsFetch = RelayChainMerge.NeedsRefresh(existing);
        // A re-submission after the chain was final adds an entry without it.
        if (!needsFetch && claimed.All(p => p.RelayChain != null)) return false;

        // FloPlayerId comes from the launcher; only flo's own roster says whose id it is.
        roster ??= await floStats.FetchGamePlayers(floGameId);
        if (roster == null)
        {
            Log.Information("RelayTelemetry: no flo roster for game {GameId}; player {PlayerId} retries on the next trigger", floGameId, floPlayerId);
            return true;
        }
        var entries = roster.TryGetValue(floPlayerId, out var owner)
            ? claimed.Where(p => string.Equals(p.BattleTag, owner, StringComparison.OrdinalIgnoreCase)).ToList()
            : [];
        if (entries.Count == 0)
        {
            Log.Warning("RelayTelemetry: game {GameId} flo player {PlayerId} is {Owner}, not the reporter {Claimants}; ignoring",
                floGameId, floPlayerId, owner, claimed.Select(p => p.BattleTag).Distinct());
            return false;
        }

        existing = CurrentChain(entries);
        if (!RelayChainMerge.NeedsRefresh(existing))
        {
            if (entries.Any(p => p.RelayChain == null))
            {
                await store.UpdatePlayerRelayChain(report.Id, floPlayerId, Tags(entries), existing);
            }
            return false;
        }

        var reply = await client.GetGameRelayTelemetry(floGameId, floPlayerId);
        if (reply == null) return true;
        if (reply.Connections.Count == 0)
        {
            // Nothing to add; writing would only blur "not fetched yet" with "fetched, empty".
            Log.Information("RelayTelemetry: controller returned no connections for game {GameId} player {PlayerId}", floGameId, floPlayerId);
            return true;
        }

        var merged = RelayChainMerge.Merge(existing, RelayChainMapper.FromReply(reply, DateTime.UtcNow));
        await store.UpdatePlayerRelayChain(report.Id, floPlayerId, Tags(entries), merged);
        return RelayChainMerge.NeedsRefresh(merged);
    }

    // A player who submitted twice has two entries; every update writes all of them.
    private static IReadOnlyCollection<string> Tags(IEnumerable<LagReportPlayer> entries) =>
        entries.Select(p => p.BattleTag).Distinct().ToList();

    private static PlayerRelayChain CurrentChain(IEnumerable<LagReportPlayer> entries) =>
        entries.Select(p => p.RelayChain).FirstOrDefault(c => c != null);
}
