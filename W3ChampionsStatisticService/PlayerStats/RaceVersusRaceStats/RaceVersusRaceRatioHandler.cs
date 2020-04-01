using System.Threading.Tasks;
using W3ChampionsStatisticService.MatchEvents;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Ports;

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
            var dataPlayers = nextEvent.data.players;
            if (dataPlayers.Count == 2)
            {
                var p1 = await _playerRepository.LoadRaceStat(dataPlayers[0].battleTag) ?? PlayerRaceLossRatio.Create(dataPlayers[0].battleTag);
                var p2 = await _playerRepository.LoadRaceStat(dataPlayers[1].battleTag) ?? PlayerRaceLossRatio.Create(dataPlayers[1].battleTag);

                p1.AddRaceWin(dataPlayers[0].won, (Race) dataPlayers[0].raceId, (Race) dataPlayers[1].raceId);
                p2.AddRaceWin(dataPlayers[1].won, (Race) dataPlayers[1].raceId, (Race) dataPlayers[0].raceId);

                await _playerRepository.UpsertRaceStat(p1);
                await _playerRepository.UpsertRaceStat(p2);
            }
        }
    }
}