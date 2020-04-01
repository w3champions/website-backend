using System.Threading.Tasks;
using W3ChampionsStatisticService.MatchEvents;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.PlayerStats.RaceOnMapStats
{
    public class RaceOnMapRatioHandler : IReadModelHandler
    {
        private readonly IPlayerStatsRepository _playerRepository;

        public RaceOnMapRatioHandler(
            IPlayerStatsRepository playerRepository
            )
        {
            _playerRepository = playerRepository;
        }

        public async Task Update(MatchFinishedEvent nextEvent)
        {
            foreach (var playerRaw in nextEvent.data.players)
            {
                var player = await _playerRepository.LoadMapStat(playerRaw.battleTag)
                             ?? RaceOnMapRatio.Create(playerRaw.battleTag);

                player.AddMapWin(playerRaw.won, (Race) playerRaw.raceId, nextEvent.data.mapInfo.name);

                await _playerRepository.UpsertMapStat(player);
            }
        }
    }
}