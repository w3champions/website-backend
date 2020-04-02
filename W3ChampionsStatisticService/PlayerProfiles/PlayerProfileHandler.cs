using System.Threading.Tasks;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.MatchEvents;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.PlayerProfiles
{
    public class PlayerModelHandler : IReadModelHandler
    {
        private readonly IPlayerRepository _playerRepository;

        public PlayerModelHandler(
            IPlayerRepository playerRepository
            )
        {
            _playerRepository = playerRepository;
        }

        public async Task Update(MatchFinishedEvent nextEvent)
        {
            foreach (var playerRaw in nextEvent.match.players)
            {
                var player = await _playerRepository.Load(playerRaw.battleTag) ?? PlayerProfile.Create(playerRaw.battleTag);
                player.RecordWin(
                    (Race) playerRaw.race,
                    GameMode.GM_1v1,
                    playerRaw.won,
                    (int) playerRaw.updatedMmr.rating);
                await _playerRepository.UpsertPlayer(player);
            }
        }
    }
}