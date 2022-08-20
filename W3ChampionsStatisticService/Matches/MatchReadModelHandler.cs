using System;
using System.Threading.Tasks;
using W3C.Domain.MatchmakingService;
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
            try
            {
                if (nextEvent.WasFakeEvent) return;
                var matchup = Matchup.Create(nextEvent);

                await _matchRepository.Insert(matchup);
                await _matchRepository.DeleteOnGoingMatch(matchup.MatchId);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
    }
}