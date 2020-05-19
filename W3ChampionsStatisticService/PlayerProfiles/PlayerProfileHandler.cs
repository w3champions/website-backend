using System.Threading.Tasks;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.PlayerProfiles
{
    public class PlayerProfileHandler : IReadModelHandler
    {
        private readonly IPlayerRepository _playerRepository;

        public PlayerProfileHandler(
            IPlayerRepository playerRepository
            )
        {
            _playerRepository = playerRepository;
        }

        public async Task Update(MatchFinishedEvent nextEvent)
        {
            foreach (var playerRaw in nextEvent.match.players)
            {
                var player = await _playerRepository.LoadPlayerProfile(playerRaw.battleTag)
                             ?? PlayerProfile.Create(playerRaw.battleTag);
                player.RecordWin(
                    playerRaw.race,
                    nextEvent.match.season, playerRaw.won);
                await _playerRepository.UpsertPlayer(player);
            }
        }
    }
}