using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Serilog;
using W3C.Contracts.Matchmaking;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Hubs;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Ladder;

public interface IFriendRankPromotionNotifier
{
    Task ObserveSyncedRanks(List<Rank> ranks);
}

/// <summary>Detects league promotions of single-member standings and pushes a
/// <see cref="FriendRankPromotedEvent"/> to the promoted player's online friends.
///
/// Detection state is self-owned: each synced batch is diffed against the
/// <see cref="LeagueBaseline"/> collection (last league seen per standing, id mirroring
/// <see cref="Rank"/>'s), then the baselines advance. A standing without a baseline —
/// season start, or the first batch after deploy — records silently. Baselines advance
/// BEFORE any push, so delivery is at most once: a failed push never resurfaces as a
/// stale diff on a later batch.
///
/// Only single-member standings (1v1, solo ranked-teams, FFA) are announced: a team is the
/// ranked entity of its standing, so a team promotion is not a fact about one friend alone.
/// Promotions only; demotions and same-tier division shuffles stay silent (but still
/// advance the baseline).
///
/// The entry point swallows its own failures: rank syncing must never depend on
/// promotion detection.</summary>
[Trace]
public class FriendRankPromotionNotifier(
    ILeagueBaselineRepository baselineRepository,
    IRankRepository rankRepository,
    IFriendRepository friendRepository,
    ConnectionMapping connections,
    IHubContext<WebsiteBackendHub> hubContext) : IFriendRankPromotionNotifier
{
    private readonly ILeagueBaselineRepository _baselineRepository = baselineRepository;
    private readonly IRankRepository _rankRepository = rankRepository;
    private readonly IFriendRepository _friendRepository = friendRepository;
    private readonly ConnectionMapping _connections = connections;
    private readonly IHubContext<WebsiteBackendHub> _hubContext = hubContext;

    public async Task ObserveSyncedRanks(List<Rank> ranks)
    {
        try
        {
            // A backlogged batch can carry the same standing several times; the bulk upsert
            // makes the last occurrence win, so detection considers only that one.
            var latest = ranks
                .Where(IsSingleMemberStanding)
                .GroupBy(r => r.Id)
                .Select(g => g.Last())
                .ToList();
            if (latest.Count == 0) return;

            var baselineById = (await _baselineRepository.LoadByIds(latest.Select(r => r.Id).ToList()))
                .ToDictionary(b => b.Id);

            var changed = latest
                .Where(r => baselineById.TryGetValue(r.Id, out var baseline) && baseline.League != r.League)
                .ToList();

            // Advance new and changed baselines only — write cost tracks transitions and
            // first sightings, not roster size.
            var moved = latest
                .Where(r => !baselineById.TryGetValue(r.Id, out var baseline) || baseline.League != r.League)
                .Select(r => new LeagueBaseline(r.Id, r.League))
                .ToList();
            if (moved.Count == 0) return;
            await _baselineRepository.UpsertMany(moved);

            if (changed.Count == 0) return;

            var leaguesByConstellation = await LoadLeagues(changed);
            var onlineConnectionsByBattleTag = _connections.GetUsers()
                .GroupBy(u => u.BattleTag)
                .ToDictionary(g => g.Key, g => g.Select(u => u.ConnectionId).ToList());

            foreach (var rank in changed)
            {
                var baseline = baselineById[rank.Id];
                if (!leaguesByConstellation.TryGetValue((rank.Season, rank.Gateway, rank.GameMode), out var leagues)) continue;
                if (!leagues.TryGetValue(baseline.League, out var oldLeague)) continue;
                if (!leagues.TryGetValue(rank.League, out var newLeague)) continue;
                // League order counts down toward the top (0 = highest league).
                if (newLeague.Order >= oldLeague.Order) continue;

                await PushToOnlineFriends(rank, oldLeague, newLeague, onlineConnectionsByBattleTag);
            }
        }
        catch (Exception e)
        {
            Log.Warning(e, "Friend rank promotion: observing synced ranks failed");
        }
    }

    // A standing's ranked entity is its member list; Player2Id is null exactly for one-member standings.
    private static bool IsSingleMemberStanding(Rank rank) => rank.Player2Id == null;

    private async Task<Dictionary<(int Season, GateWay Gateway, GameMode GameMode), Dictionary<int, League>>> LoadLeagues(List<Rank> changed)
    {
        var leaguesByConstellation = new Dictionary<(int, GateWay, GameMode), Dictionary<int, League>>();
        foreach (var season in changed.Select(r => r.Season).Distinct())
        {
            var constellations = await _rankRepository.LoadLeagueConstellation(season);
            foreach (var constellation in constellations)
            {
                leaguesByConstellation[(constellation.Season, constellation.Gateway, constellation.GameMode)] =
                    constellation.Leagues.ToDictionary(l => l.Id);
            }
        }
        return leaguesByConstellation;
    }

    private async Task PushToOnlineFriends(
        Rank rank,
        League oldLeague,
        League newLeague,
        Dictionary<string, List<string>> onlineConnectionsByBattleTag)
    {
        var promotedBattleTag = rank.Player1Id;
        var friendlistsContainingPlayer = await _friendRepository.LoadFriendlistsContaining(promotedBattleTag);
        if (friendlistsContainingPlayer.Count == 0) return;

        var payload = new FriendRankPromotedEvent
        {
            BattleTag = promotedBattleTag,
            Season = rank.Season,
            Gateway = rank.Gateway,
            GameMode = rank.GameMode,
            Race = rank.Race,
            LeagueName = newLeague.Name,
            LeagueOrder = newLeague.Order,
            LeagueDivision = newLeague.Division,
            OldLeagueName = oldLeague.Name,
            OldLeagueOrder = oldLeague.Order,
            OldLeagueDivision = oldLeague.Division,
        };

        foreach (var friendlist in friendlistsContainingPlayer)
        {
            if (friendlist.Id == promotedBattleTag) continue;
            if (!onlineConnectionsByBattleTag.TryGetValue(friendlist.Id, out var connectionIds)) continue;
            foreach (var connectionId in connectionIds)
            {
                await _hubContext.Clients.Client(connectionId)
                    .SendAsync(WebsiteBackendSocketResponseType.FriendRankPromoted.ToString(), payload);
            }
        }
    }
}
