using System.Threading.Tasks;
using W3ChampionsStatisticService.MatchEvents;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats
{
    public class RaceOnMapVersusRaceRatioHandler : IReadModelHandler
    {
        private readonly IPlayerStatsRepository _playerRepository;

        public RaceOnMapVersusRaceRatioHandler(
            IPlayerStatsRepository playerRepository
            )
        {
            _playerRepository = playerRepository;
        }

        public async Task Update(MatchFinishedEvent nextEvent)
        {
            var dataPlayers = nextEvent.match.players;
            if (dataPlayers.Count == 2)
            {
                var p1 = await _playerRepository.LoadMapAndRaceStat(dataPlayers[0].battleTag)
                         ?? RaceOnMapVersusRaceRatio.Create (dataPlayers[0].battleTag);
                var p2 = await _playerRepository.LoadMapAndRaceStat(dataPlayers[1].battleTag)
                         ?? RaceOnMapVersusRaceRatio.Create(dataPlayers[1].battleTag);

                p1.AddMapWin(dataPlayers[0].won,
                    (Race) dataPlayers[0].race,
                    (Race) dataPlayers[1].race,
                    nextEvent.match.map);
                p2.AddMapWin(dataPlayers[1].won,
                    (Race) dataPlayers[1].race,
                    (Race) dataPlayers[0].race,
                    nextEvent.match.map);

                await _playerRepository.UpsertMapAndRaceStat(p1);
                await _playerRepository.UpsertMapAndRaceStat(p2);
            }
        }
    }
}