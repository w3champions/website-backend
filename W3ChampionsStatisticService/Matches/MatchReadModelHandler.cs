using System.Threading.Tasks;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

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
            var matchup = new Matchup(nextEvent);
            await _matchRepository.Insert(matchup);
        }

    }
}