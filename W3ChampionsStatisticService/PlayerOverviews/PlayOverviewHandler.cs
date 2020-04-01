using System.Threading.Tasks;
using W3ChampionsStatisticService.MatchEvents;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.PlayerOverviews
{
    public class PlayOverviewHandler : IReadModelHandler
    {
        private readonly IPlayerRepository _playerRepository;

        public PlayOverviewHandler(
            IPlayerRepository playerRepository
            )
        {
            _playerRepository = playerRepository;
        }

        public async Task Update(MatchFinishedEvent nextEvent)
        {
            foreach (var playerRaw in nextEvent.match.players)
            {
                var player = await _playerRepository.LoadOverview(playerRaw.battleTag) ?? new PlayerOverview(playerRaw.battleTag);
                player.RecordWin(playerRaw.won);
                await _playerRepository.UpsertPlayer(player);
            }
        }
    }
}