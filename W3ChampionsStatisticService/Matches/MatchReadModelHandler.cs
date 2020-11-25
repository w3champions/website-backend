using System.Threading.Tasks;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Matches
{
    public class MatchReadModelHandler : IReadModelHandler
    {
        private readonly IMatchRepository _matchRepository;
        private readonly IMatchEventRepository _matchEventRepository;

        public MatchReadModelHandler(
            IMatchRepository matchRepository,
            IMatchEventRepository matchEventRepository
            )
        {
            _matchRepository = matchRepository;
            _matchEventRepository = matchEventRepository;
        }

        public async Task Update(MatchFinishedEvent nextEvent)
        {
            if (nextEvent.WasFakeEvent) return;
            var count = await _matchRepository.Count();
            var matchup = Matchup.Create(nextEvent);

            nextEvent.match.number = count + 1;

            await _matchEventRepository.Upsert(nextEvent);
            await _matchRepository.Insert(matchup);
            await _matchRepository.DeleteOnGoingMatch(matchup.MatchId);
        }
    }
}