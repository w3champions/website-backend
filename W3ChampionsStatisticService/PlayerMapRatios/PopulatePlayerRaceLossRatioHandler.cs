using System.Threading.Tasks;
using W3ChampionsStatisticService.MatchEvents;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.PlayerMapRatios
{
    public class PopulatePlayerMapRatioHandler : IReadModelHandler
    {
        private readonly IPlayerRepository _playerRepository;

        public PopulatePlayerMapRatioHandler(
            IPlayerRepository playerRepository
            )
        {
            _playerRepository = playerRepository;
        }

        public async Task Update(MatchFinishedEvent nextEvent)
        {
            foreach (var playerRaw in nextEvent.data.players)
            {
                var player = await _playerRepository.LoadMapStat(playerRaw.battleTag)
                             ?? PlayerMapRatio.Create(playerRaw.battleTag);

                player.AddMapWin(playerRaw.won, (Race) playerRaw.raceId, nextEvent.data.mapInfo.name);

                await _playerRepository.UpsertMapStat(player);
            }
        }
    }
}