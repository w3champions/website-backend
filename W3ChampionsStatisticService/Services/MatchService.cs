using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3C.Domain.GameModes;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Cache;
using W3ChampionsStatisticService.Heroes;
using W3ChampionsStatisticService.Maps;
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
    ICachedDataProvider<List<MapIdAndName>> cachedMapIdsAndNamesProvider,
    IPersonalSettingsRepository personalSettingsRepository,
    ICurrentMapNameProvider currentMapNameProvider)
{
    private readonly IMatchRepository _matchRepository = matchRepository;
    private readonly ICachedDataProvider<List<Matchup>> _cachedMatchesProvider = cachedMatchesProvider;
    private readonly ICachedDataProvider<CachedLong> _cachedMatchCountProvider = cachedMatchCountProvider;
    private readonly ICachedDataProvider<List<MapIdAndName>> _cachedMapIdsAndNamesProvider = cachedMapIdsAndNamesProvider;
    private readonly IPersonalSettingsRepository _personalSettingsRepository = personalSettingsRepository;
    private readonly ICurrentMapNameProvider _currentMapNameProvider = currentMapNameProvider;

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

    // The names a season and mode displays: current names for current-name modes, stored names otherwise.
    public async Task<List<string>> GetMapNames(int season, GameMode gameMode)
    {
        var mapIdsAndNames = await GetMapIdsAndNames(season, gameMode);
        var currentMapNames = await _currentMapNameProvider.GetAsync();

        return mapIdsAndNames
            .Select(m => currentMapNames.Resolve(gameMode, m.MapId, m.MapName))
            .Where(mapName => !string.IsNullOrEmpty(mapName))
            .Distinct()
            .OrderBy(mapName => mapName)
            .ToList();
    }

    // Null when there is nothing to filter, or when the mode displays stored names (then the stored name is matched).
    // Built from the same pairs as GetMapNames, so every listed name finds its matches.
    public async Task<MapNameFilter> GetMapNameFilter(int season, GameMode gameMode, string mapName)
    {
        if (string.IsNullOrWhiteSpace(mapName) || mapName == "Overall" || !GameModesHelper.UsesCurrentMapName(gameMode))
        {
            return null;
        }

        var mapIdsAndNames = await GetMapIdsAndNames(season, gameMode);
        var currentMapNames = await _currentMapNameProvider.GetAsync();

        var mapIdsWithCurrentName = mapIdsAndNames
            .Where(m => currentMapNames.HasCurrentName(m.MapId))
            .Select(m => m.MapId.Value)
            .ToHashSet();
        var mapIds = mapIdsWithCurrentName
            .Where(mapId => currentMapNames.Resolve(mapId, null) == mapName)
            .ToHashSet();

        return new MapNameFilter(mapName, mapIds, mapIdsWithCurrentName);
    }

    private Task<List<MapIdAndName>> GetMapIdsAndNames(int season, GameMode gameMode)
    {
        string cacheKey = $"map_ids_and_names_{season}_{gameMode}";

        return _cachedMapIdsAndNamesProvider.GetCachedOrRequestAsync(
            async () => await _matchRepository.LoadMapIdsAndNames(season, gameMode),
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
