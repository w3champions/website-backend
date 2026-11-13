using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Maps;
using W3ChampionsStatisticService.PlayerStats.HeroStats;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.W3ChampionsStats.MapsPerSeasons;
using W3C.Domain.Tracing;


namespace W3ChampionsStatisticService.Services;

[Trace]
public class PlayerStatisticsService(
    IPlayerStatsRepository playerStatsRepository,
    IW3StatsRepo w3StatsRepo,
    ICurrentMapNameProvider currentMapNameProvider)
{
    private readonly IPlayerStatsRepository _playerStatsRepository = playerStatsRepository;
    private readonly IW3StatsRepo _w3StatsRepo = w3StatsRepo;
    private readonly ICurrentMapNameProvider _currentMapNameProvider = currentMapNameProvider;

    // Stored map names are from match time; entries with a known map id display the current name.
    public async Task<PlayerRaceOnMapVersusRaceRatioView> GetMapAndRaceStatAsync(string battleTag, int season)
    {
        var mapAndRaceStat = await _playerStatsRepository.LoadMapAndRaceStat(battleTag, season);

        var view = mapAndRaceStat == null ?
        PlayerRaceOnMapVersusRaceRatioView.Create(battleTag, season) :
        PlayerRaceOnMapVersusRaceRatioView.Create(mapAndRaceStat);

        (await _currentMapNameProvider.GetAsync()).Apply(view);
        return view;
    }

    // Stored map names are from match time; entries with a known map id display the current name.
    public async Task<PlayerHeroStats> GetHeroStatAsync(string battleTag, int season)
    {
        var heroStats = await _playerStatsRepository.LoadHeroStat(battleTag, season) ?? PlayerHeroStats.Create(battleTag, season);

        (await _currentMapNameProvider.GetAsync()).Apply(heroStats);
        return heroStats;
    }

    // Stored map names are from match time; current-name modes display the current name.
    public async Task<List<MapsPerSeason>> LoadMatchesOnMapAsync()
    {
        var matchesOnMap = await _w3StatsRepo.LoadMatchesOnMap();

        (await _currentMapNameProvider.GetAsync()).Apply(matchesOnMap);
        return matchesOnMap;
    }

}
