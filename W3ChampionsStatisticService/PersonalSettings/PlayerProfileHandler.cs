using System.Threading.Tasks;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.PersonalSettings
{
    public class PlayerRaceWinModelHandler : IReadModelHandler
    {
        private readonly IPlayerRepository _playerRepository;

        public PlayerRaceWinModelHandler(
            IPlayerRepository playerRepository
            )
        {
            _playerRepository = playerRepository;
        }

        public async Task Update(MatchFinishedEvent nextEvent)
        {
            foreach (var playerRaw in nextEvent.match.players)
            {
                var player = await _playerRepository.LoadPlayerRaceWins(playerRaw.battleTag)
                             ?? PlayerRaceWins.Create(playerRaw.battleTag);
                player.RecordWin(
                    playerRaw.race,
                    playerRaw.won);
                await _playerRepository.UpsertPlayerRaceWin(player);
            }
        }
    }
}