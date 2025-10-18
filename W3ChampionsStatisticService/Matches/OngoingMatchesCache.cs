using W3ChampionsStatisticService.Common.Constants;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3C.Contracts.Matchmaking;
using W3C.Domain.Repositories;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Services;

namespace W3ChampionsStatisticService.Matches;

[Trace]
public class OngoingMatchesCache(MongoClient mongoClient, TracingService tracingService) : MongoDbRepositoryBase(mongoClient), IOngoingMatchesCache
{
    private readonly TracingService _tracingService = tracingService; // Unused for now, will be used in the future
    private List<OnGoingMatchup> _values = [];
    private readonly object _lock = new();

    public async Task<long> CountOnGoingMatches(
        GameMode gameMode,
        GateWay gateWay,
        string map,
        int minMmr,
        int? maxMmr)
    {
        await UpdateCacheIfNeeded();
        if (maxMmr == null) maxMmr = MmrConstants.MaxMmrPerGameMode[gameMode];
        return _values.Count(m => (gameMode == GameMode.Undefined || m.GameMode == gameMode)
                                    && (gateWay == GateWay.Undefined || m.GateWay == gateWay)
                                    && (map == "Overall" || m.Map == map)
                                    && (minMmr == 0 || !m.Teams.Any(team => team.Players.Any(player => player.OldMmr != null && player.OldMmr < minMmr)))
                                    && (maxMmr == MmrConstants.MaxMmrPerGameMode[gameMode] || !m.Teams.Any(team => team.Players.Any(player => player.OldMmr == null || player.OldMmr > maxMmr))));
    }

    public async Task<List<OnGoingMatchup>> LoadOnGoingMatches(
        GameMode gameMode,
        GateWay gateWay,
        int offset,
        int pageSize,
        string map,
        int minMmr,
        int? maxMmr,
        string sort)
    {
        await UpdateCacheIfNeeded();
        if (maxMmr == null) maxMmr = MmrConstants.MaxMmrPerGameMode[gameMode];
        var matches = _values
            .Where(m => (gameMode == GameMode.Undefined || m.GameMode == gameMode)
                        && (gateWay == GateWay.Undefined || m.GateWay == gateWay)
                        && (map == "Overall" || m.Map == map)
                        && (minMmr == 0 || !m.Teams.Any(team => team.Players.Any(player => player.OldMmr != null && player.OldMmr < minMmr)))
                        && (maxMmr == MmrConstants.MaxMmrPerGameMode[gameMode] || !m.Teams.Any(team => team.Players.Any(player => player.OldMmr == null || player.OldMmr > maxMmr))));

        if (sort == "mmrDescending")
        {
            matches = matches.OrderByDescending(m => GetMaxMmrInMatch(m));
        }

        return matches
            .Skip(offset)
            .Take(pageSize)
            .ToList();
    }

    [NoTrace]
    public int GetMaxMmrInTeam(Team team)
    {
        return team.Players.Max(p => p.OldMmr ?? 0);
    }

    [NoTrace]
    public int GetMaxMmrInMatch(OnGoingMatchup match)
    {
        return match.Teams.Max(t => GetMaxMmrInTeam(t));
    }

    public async Task<OnGoingMatchup> LoadOnGoingMatchForPlayer(string playerId)
    {
        await UpdateCacheIfNeeded();
        return _values.FirstOrDefault(m => m.Team1Players != null && m.Team1Players.Contains(playerId)
                                        || m.Team2Players != null && m.Team2Players.Contains(playerId)
                                        || m.Team3Players != null && m.Team3Players.Contains(playerId)
                                        || m.Team4Players != null && m.Team4Players.Contains(playerId));
    }

    public void Upsert(OnGoingMatchup matchup)
    {
        lock (_lock)
        {
            var orderByDescending = _values.Where(m => m.MatchId != matchup.MatchId);
            _values = orderByDescending.Append(matchup).OrderByDescending(s => s.Id).ToList();
        }

    }

    public void Delete(string matchId)
    {
        lock (_lock)
        {
            _values = _values.Where(m => m.MatchId != matchId).ToList();
        }
    }

    [NoTrace]
    private async Task UpdateCacheIfNeeded()
    {
        if (_values.Count == 0)
        {
            var mongoCollection = CreateCollection<OnGoingMatchup>();
            var values = await mongoCollection.Find(r => true).SortByDescending(s => s.Id).ToListAsync();
            lock (_lock)
            {
                _values = values;
            }
        }
    }
}

public interface IOngoingMatchesCache
{
    Task<long> CountOnGoingMatches(
        GameMode gameMode,
        GateWay gateWay,
        string map,
        int minMmr,
        int? maxMmr);

    Task<List<OnGoingMatchup>> LoadOnGoingMatches(
        GameMode gameMode,
        GateWay gateWay,
        int offset,
        int pageSize,
        string map,
        int minMmr,
        int? maxMmr,
        string sort);

    Task<OnGoingMatchup> LoadOnGoingMatchForPlayer(string playerId);
    void Upsert(OnGoingMatchup matchup);
    void Delete(string matchId);
}
