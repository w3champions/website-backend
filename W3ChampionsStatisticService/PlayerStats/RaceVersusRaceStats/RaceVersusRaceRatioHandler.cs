using System.Threading.Tasks;
using W3ChampionsStatisticService.MatchEvents;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.PlayerStats.RaceVersusRaceStats
{
    public class RaceVersusRaceRatioHandler : IReadModelHandler
    {
        private readonly IPlayerStatsRepository _playerRepository;

        public RaceVersusRaceRatioHandler(
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
                var p1 = await _playerRepository.LoadRaceStat(dataPlayers[0].battleTag) ?? RaceVersusRaceRatio.Create(dataPlayers[0].battleTag);
                var p2 = await _playerRepository.LoadRaceStat(dataPlayers[1].battleTag) ?? RaceVersusRaceRatio.Create(dataPlayers[1].battleTag);

                p1.AddRaceWin((Race) dataPlayers[0].race, (Race) dataPlayers[1].race, dataPlayers[0].won);
                p2.AddRaceWin((Race) dataPlayers[1].race, (Race) dataPlayers[0].race, dataPlayers[1].won);

                await _playerRepository.UpsertRaceStat(p1);
                await _playerRepository.UpsertRaceStat(p2);
            }
        }
    }
}