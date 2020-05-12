using System.Threading.Tasks;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.PersonalSettings
{
    public class PlayerRaceWinModelHandler : IReadModelHandler
    {
        private readonly IPersonalSettingsRepository _playerRepository;

        public PlayerRaceWinModelHandler(
            IPersonalSettingsRepository playerRepository
            )
        {
            _playerRepository = playerRepository;
        }

        public async Task Update(MatchFinishedEvent nextEvent)
        {
            //beta disabled for avatars
            if (nextEvent.match.season == 0) return;

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