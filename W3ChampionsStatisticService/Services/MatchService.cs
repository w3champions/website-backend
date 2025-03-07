using System;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Cache;
using System.Collections.Generic;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Services;

// ICachedDataProvider can't cache primitives so we wrap long
public class CachedLong {
    public long Value { get; set; }
}

// Calls MatchRepository and caches results
public class MatchService(
    IMatchRepository matchRepository,
    ICachedDataProvider<List<Matchup>> cachedMatchesProvider,
    ICachedDataProvider<CachedLong> cachedMatchCountProvider) {
    private readonly IMatchRepository _matchRepository = matchRepository;
    private readonly ICachedDataProvider<List<Matchup>> _cachedMatchesProvider = cachedMatchesProvider;
    private readonly ICachedDataProvider<CachedLong> _cachedMatchCountProvider = cachedMatchCountProvider;

    public async Task<List<Matchup>> GetMatchesPerPlayer(
        string playerId,
        int season,
        string opponentId,
        GameMode gameMode,
        GateWay gateWay,
        Race playerRace,
        Race opponentRace,
        int offset,
        int pageSize) {
        // Generate a unique cache key based on the request parameters
        string cacheKeyMatches =
            $"matches_{playerId}_{season}_{opponentId}_{gameMode}_{gateWay}_{playerRace}_{opponentRace}_{offset}_{pageSize}";

        var matches = await _cachedMatchesProvider.GetCachedOrRequestAsync(
            async () => await _matchRepository.LoadFor(
                playerId,
                opponentId,
                gateWay,
                gameMode,
                playerRace,
                opponentRace,
                pageSize,
                offset,
                season), cacheKeyMatches, TimeSpan.FromSeconds(15));
        return matches;
    }

    public async Task<long> GetMatchCountPerPlayer(
        string playerId,
        int season,
        string opponentId,
        GameMode gameMode,
        GateWay gateWay,
        Race playerRace,
        Race opponentRace) {
        // Generate a unique cache key based on the request parameters
        string cacheKeyCount =
            $"count_{playerId}_{season}_{opponentId}_{gameMode}_{gateWay}_{playerRace}_{opponentRace}";

        var count = await _cachedMatchCountProvider.GetCachedOrRequestAsync(
            async () => new CachedLong {
                Value = await _matchRepository.CountFor(playerId, opponentId, gateWay, gameMode, playerRace,
                    opponentRace, season)
            }, cacheKeyCount, TimeSpan.FromSeconds(15));
        return count.Value;
    }
}