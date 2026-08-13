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
    Task<List<Rank>> CaptureOldRanks(List<Rank> newRanks);
    Task NotifyPromotions(List<Rank> newRanks, List<Rank> oldRanks);
}

/// <summary>Detects league promotions of single-member standings during rank syncing and pushes
/// a <see cref="FriendRankPromotedEvent"/> to the promoted player's online friends.
///
/// Only single-member standings (1v1, solo ranked-teams, FFA) are announced: a team is the
/// ranked entity of its standing, so a team promotion is not a fact about one friend alone.
/// Promotions only; demotions and same-tier division shuffles stay silent.
///
/// Both entry points swallow their own failures: rank syncing must never depend on
/// promotion detection.</summary>
[Trace]
public class FriendRankPromotionNotifier(
    IRankRepository rankRepository,
    IFriendRepository friendRepository,
    ConnectionMapping connections,
    IHubContext<WebsiteBackendHub> hubContext) : IFriendRankPromotionNotifier
{
    private readonly IRankRepository _rankRepository = rankRepository;
    private readonly IFriendRepository _friendRepository = friendRepository;
    private readonly ConnectionMapping _connections = connections;
    private readonly IHubContext<WebsiteBackendHub> _hubContext = hubContext;

    /// <summary>Snapshots the stored single-member standings that the coming
    /// <see cref="IRankRepository.InsertRanks"/> will replace. One indexed _id read per batch.</summary>
    public async Task<List<Rank>> CaptureOldRanks(List<Rank> newRanks)
    {
        try
        {
            var ids = newRanks.Where(IsSingleMemberStanding).Select(r => r.Id).Distinct().ToList();
            if (ids.Count == 0) return [];
            return await _rankRepository.LoadRanksByIds(ids);
        }
        catch (Exception e)
        {
            Log.Warning(e, "Friend rank promotion: capturing pre-sync ranks failed");
            return [];
        }
    }

    public async Task NotifyPromotions(List<Rank> newRanks, List<Rank> oldRanks)
    {
        try
        {
            if (oldRanks == null || oldRanks.Count == 0) return;
            var oldById = oldRanks.ToDictionary(r => r.Id);

            // A backlogged batch can carry the same standing several times; the bulk upsert
            // makes the last occurrence win, so the diff compares against that one.
            var changed = newRanks
                .Where(IsSingleMemberStanding)
                .GroupBy(r => r.Id)
                .Select(g => g.Last())
                .Where(r => oldById.TryGetValue(r.Id, out var old) && old.League != r.League)
                .ToList();
            if (changed.Count == 0) return;

            var leaguesByConstellation = await LoadLeagues(changed);
            var onlineConnectionsByBattleTag = _connections.GetUsers()
                .GroupBy(u => u.BattleTag)
                .ToDictionary(g => g.Key, g => g.Select(u => u.ConnectionId).ToList());

            foreach (var rank in changed)
            {
                var old = oldById[rank.Id];
                if (!leaguesByConstellation.TryGetValue((rank.Season, rank.Gateway, rank.GameMode), out var leagues)) continue;
                if (!leagues.TryGetValue(old.League, out var oldLeague)) continue;
                if (!leagues.TryGetValue(rank.League, out var newLeague)) continue;
                // League order counts down toward the top (0 = highest league).
                if (newLeague.Order >= oldLeague.Order) continue;

                await PushToOnlineFriends(rank, oldLeague, newLeague, onlineConnectionsByBattleTag);
            }
        }
        catch (Exception e)
        {
            Log.Warning(e, "Friend rank promotion: notification pass failed");
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
