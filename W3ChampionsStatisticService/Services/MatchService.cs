using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Cache;
using W3ChampionsStatisticService.Heroes;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Services;

// ICachedDataProvider can't cache primitives so we wrap long
public class CachedLong
{
    public long Value { get; set; }
}

// Calls MatchRepository and caches results
[Trace]
public class MatchService(
    IMatchRepository matchRepository,
    ICachedDataProvider<List<Matchup>> cachedMatchesProvider,
    ICachedDataProvider<CachedLong> cachedMatchCountProvider,
    ICachedDataProvider<List<string>> cachedMapNamesProvider,
    ICachedDataProvider<List<OpponentInfo>> cachedOpponentsProvider,
    IPersonalSettingsRepository personalSettingsRepository)
{
    private readonly IMatchRepository _matchRepository = matchRepository;
    private readonly ICachedDataProvider<List<Matchup>> _cachedMatchesProvider = cachedMatchesProvider;
    private readonly ICachedDataProvider<CachedLong> _cachedMatchCountProvider = cachedMatchCountProvider;
    private readonly ICachedDataProvider<List<string>> _cachedMapNamesProvider = cachedMapNamesProvider;
    private readonly ICachedDataProvider<List<OpponentInfo>> _cachedOpponentsProvider = cachedOpponentsProvider;
    private readonly IPersonalSettingsRepository _personalSettingsRepository = personalSettingsRepository;

    public async Task<List<Matchup>> GetMatchesPerPlayer(
        string playerId,
        int season,
        string opponentId,
        GameMode gameMode,
        GateWay gateWay,
        Race playerRace,
        Race opponentRace,
        int offset,
        int pageSize,
        HeroType hero = HeroType.AllFilter,
        bool playerIncludeRandom = false,
        bool opponentIncludeRandom = false)
    {
        // Generate a unique cache key based on the request parameters
        string cacheKeyMatches =
            $"matches_{playerId}_{season}_{opponentId}_{gameMode}_{gateWay}_{playerRace}_{opponentRace}_{offset}_{pageSize}_{hero}_{playerIncludeRandom}_{opponentIncludeRandom}";

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
                season,
                hero,
                playerIncludeRandom,
                opponentIncludeRandom), cacheKeyMatches, TimeSpan.FromSeconds(5));
        return matches;
    }

    public async Task<long> GetMatchCountPerPlayer(
        string playerId,
        int season,
        string opponentId,
        GameMode gameMode,
        GateWay gateWay,
        Race playerRace,
        Race opponentRace,
        HeroType hero = HeroType.AllFilter,
        bool playerIncludeRandom = false,
        bool opponentIncludeRandom = false)
    {
        // Generate a unique cache key based on the request parameters
        string cacheKeyCount =
            $"count_{playerId}_{season}_{opponentId}_{gameMode}_{gateWay}_{playerRace}_{opponentRace}_{hero}_{playerIncludeRandom}_{opponentIncludeRandom}";

        var count = await _cachedMatchCountProvider.GetCachedOrRequestAsync(
            async () => new CachedLong
            {
                Value = await _matchRepository.CountFor(playerId, opponentId, gateWay, gameMode, playerRace,
                    opponentRace, season, hero, playerIncludeRandom, opponentIncludeRandom)
            }, cacheKeyCount, TimeSpan.FromSeconds(5));
        return count.Value;
    }

    public async Task<List<OpponentInfo>> SearchOpponentsPerPlayer(
        string playerId,
        string search,
        int season,
        GateWay gateWay,
        int limit)
    {
        string cacheKey = $"opponents_{playerId}_{season}_{gateWay}_{limit}_{search?.ToLowerInvariant()}";

        return await _cachedOpponentsProvider.GetCachedOrRequestAsync(
            async () => await _matchRepository.SearchOpponentsFor(playerId, search, season, gateWay, limit),
            cacheKey,
            TimeSpan.FromMinutes(1));
    }

    public async Task<List<string>> GetMapNames(int season, GameMode gameMode)
    {
        string cacheKey = $"map_names_{season}_{gameMode}";

        return await _cachedMapNamesProvider.GetCachedOrRequestAsync(
            async () => await _matchRepository.LoadMapNames(season, gameMode),
            cacheKey,
            TimeSpan.FromMinutes(10));
    }

    public async Task SetPlayersCountryCode(Matchup matchup)
    {
        foreach (Team team in matchup.Teams)
        {
            foreach (PlayerOverviewMatches player in team.Players)
            {
                PersonalSetting personalSettings = await _personalSettingsRepository.LoadOrCreate(player.BattleTag);
                player.CountryCode = personalSettings.CountryCode;
            }
        }
    }
}
