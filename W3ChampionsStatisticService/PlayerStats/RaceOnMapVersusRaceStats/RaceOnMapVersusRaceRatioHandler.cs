using System.Threading.Tasks;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

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
            if (nextEvent.WasFakeEvent) return;
            var dataPlayers = nextEvent.match.players;
            if (dataPlayers.Count == 2)
            {
                var p1 = await _playerRepository.LoadMapAndRaceStat(dataPlayers[0].battleTag)
                         ?? RaceOnMapVersusRaceRatio.Create(dataPlayers[0].battleTag);
                var p2 = await _playerRepository.LoadMapAndRaceStat(dataPlayers[1].battleTag)
                         ?? RaceOnMapVersusRaceRatio.Create(dataPlayers[1].battleTag);

                p1.AddMapWin(dataPlayers[0].race,
                    dataPlayers[1].race,
                    "Overall",
                    dataPlayers[0].won);
                p2.AddMapWin(dataPlayers[1].race,
                    dataPlayers[0].race,
                    "Overall",
                    dataPlayers[1].won);

                p1.AddMapWin(dataPlayers[0].race,
                    dataPlayers[1].race,
                    new MapName(nextEvent.match.map).Name,
                    dataPlayers[0].won);
                p2.AddMapWin(dataPlayers[1].race,
                    dataPlayers[0].race,
                    new MapName(nextEvent.match.map).Name,
                    dataPlayers[1].won);

                await _playerRepository.UpsertMapAndRaceStat(p1);
                await _playerRepository.UpsertMapAndRaceStat(p2);
            }
        }
    }
}