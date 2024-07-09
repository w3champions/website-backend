using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Cache;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.W3ChampionsStats.MapsPerSeasons;

namespace W3ChampionsStatisticService.Services;

public class PlayerStatisticsService
{
    private readonly IPlayerStatsRepository _playerStatsRepository;
    private readonly MatchmakingProvider _matchmakingProvider;
    private readonly ICachedDataProvider<SeasonMapInformation> _seasonMapCached;
    private readonly IW3StatsRepo _w3StatsRepo;

    public PlayerStatisticsService(IPlayerStatsRepository playerStatsRepository,
        MatchmakingProvider matchmakingProvider, ICachedDataProvider<SeasonMapInformation> seasonMapCached,
        IW3StatsRepo w3StatsRepo)
    {
        _playerStatsRepository = playerStatsRepository;
        _matchmakingProvider = matchmakingProvider;
        _seasonMapCached = seasonMapCached;
        _w3StatsRepo = w3StatsRepo;
    }

    public async Task<PlayerRaceOnMapVersusRaceRatioView> GetMapAndRaceStatAsync(string battleTag, int season)
    {
        var mapAndRaceStat = await _playerStatsRepository.LoadMapAndRaceStat(battleTag, season);

        var mapInformation = await FetchMapNamesAsync();

        return mapAndRaceStat == null ?
        PlayerRaceOnMapVersusRaceRatioView.Create(battleTag, season) :
        PlayerRaceOnMapVersusRaceRatioView.Create(mapAndRaceStat, mapInformation);
    }

    private async Task<SeasonMapInformation> FetchMapNamesAsync()
    {
        return await _seasonMapCached.GetCachedOrRequestAsync(FetchCurrentSeasonMapsInfoAsync, null);
    }

    private async Task<SeasonMapInformation> FetchCurrentSeasonMapsInfoAsync()
    {
        var gameModes = await _matchmakingProvider.GetCurrentlyActiveGameModesAsync();
        var mapNames = new Dictionary<string, string>();
        foreach (var map in gameModes.SelectMany(x => x.maps))
        {
            var mapName = new MapName(map.path).Name;
            mapNames[mapName] = map.name;
        }

        return new SeasonMapInformation(mapNames);
    }

    public async Task<List<MapsPerSeason>> LoadMatchesOnMapAsync()
    {
        var loadMatchesOnMap = await _w3StatsRepo.LoadMatchesOnMap();
        var mapInformation = await FetchMapNamesAsync();
        foreach (var mapsPerSeason in loadMatchesOnMap
                        .SelectMany(x => x.MatchesOnMapPerModes)
                        .SelectMany(x => x.Maps))
        {
            mapsPerSeason.MapName = mapInformation.GetMapName(mapsPerSeason.Map);
        }

        return loadMatchesOnMap;
    }

}
