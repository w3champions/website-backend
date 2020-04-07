using System.Linq;
using System.Threading.Tasks;
using W3ChampionsStatisticService.MatchEvents;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Ladder
{
    public class PlayerWinrateHandler : IReadModelHandler
    {
        private readonly IPlayerRepository _playerRepository;

        public PlayerWinrateHandler(
            IPlayerRepository playerRepository
            )
        {
            _playerRepository = playerRepository;
        }

        public async Task Update(MatchFinishedEvent nextEvent)
        {
            var playerMMrChanges = nextEvent.match.players;
            var winrateTasks = playerMMrChanges.Select(async p => await LoadAndApply(p));
            var newWinrates = (await Task.WhenAll(winrateTasks)).ToList();
            await _playerRepository.Save(newWinrates);
        }

        private async Task<PlayerWinLoss> LoadAndApply(PlayerMMrChange p)
        {
            var playerWinLoss = await _playerRepository.LoadPlayerWinrate(p.battleTag);
            var loadPlayerWinrate = playerWinLoss ?? PlayerWinLoss.Create(p.battleTag);
            return loadPlayerWinrate.Apply(p.won);
        }
    }
}