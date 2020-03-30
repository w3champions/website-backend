using System.Threading.Tasks;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.MatchEvents;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Players
{
    public class PopulatePlayerModelHandler : IReadModelHandler
    {
        private readonly IPlayerRepository _playerRepository;

        public PopulatePlayerModelHandler(
            IPlayerRepository playerRepository
            )
        {
            _playerRepository = playerRepository;
        }

        public async Task Update(MatchFinishedEvent nextEvent)
        {
            foreach (var playerRaw in nextEvent.data.players)
            {
                var player = await _playerRepository.Load(playerRaw.battleTag) ?? PlayerProfile.Create(playerRaw.battleTag);
                player.RecordWin((Race) playerRaw.raceId, GameMode.GM_1v1, playerRaw.won);
                await _playerRepository.UpsertPlayer(player);
            }
        }
    }
}