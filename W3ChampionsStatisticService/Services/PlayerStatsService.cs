using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.W3ChampionsStats.MapsPerSeasons;
using W3C.Domain.Tracing;


namespace W3ChampionsStatisticService.Services;

[Trace]
public class PlayerStatisticsService(
    IPlayerStatsRepository playerStatsRepository,
    IW3StatsRepo w3StatsRepo)
{
    private readonly IPlayerStatsRepository _playerStatsRepository = playerStatsRepository;
    private readonly IW3StatsRepo _w3StatsRepo = w3StatsRepo;

    public async Task<PlayerRaceOnMapVersusRaceRatioView> GetMapAndRaceStatAsync(string battleTag, int season)
    {
        var mapAndRaceStat = await _playerStatsRepository.LoadMapAndRaceStat(battleTag, season);

        return mapAndRaceStat == null ?
        PlayerRaceOnMapVersusRaceRatioView.Create(battleTag, season) :
        PlayerRaceOnMapVersusRaceRatioView.Create(mapAndRaceStat);
    }

    // MapName is stored per entry from the MatchFinishedEvent, so it is returned as is.
    public async Task<List<MapsPerSeason>> LoadMatchesOnMapAsync()
    {
        return await _w3StatsRepo.LoadMatchesOnMap();
    }

}
