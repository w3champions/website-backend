using System.Linq;
using System.Threading.Tasks;
using W3ChampionsStatisticService.MatchEvents;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Matches
{
    public class MatchReadModelHandler : IReadModelHandler
    {
        private readonly IMatchRepository _matchRepository;

        public MatchReadModelHandler(
            IMatchRepository matchRepository
            )
        {
            _matchRepository = matchRepository;
        }

        public async Task Update(MatchFinishedEvent nextEvent)
        {
            var playerMMrChanges = nextEvent.match.players;
            var winrateTasks = playerMMrChanges.Select(async p => await LoadAndApply(p));
            var newWinrates = (await Task.WhenAll(winrateTasks)).ToList();
            await _matchRepository.Save(newWinrates);

            await _matchRepository.Insert(new Matchup(nextEvent, newWinrates));
        }

        private async Task<PlayerWinLoss> LoadAndApply(PlayerMMrChange p)
        {
            var playerWinLoss = await _matchRepository.LoadPlayerWinrate(p.battleTag);
            var loadPlayerWinrate = playerWinLoss ?? PlayerWinLoss.Create(p.battleTag);
            return loadPlayerWinrate.Apply(p.won);
        }
    }
}