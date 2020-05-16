using System.Threading.Tasks;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Matches
{
    public class MatchReadModelStartedMatchesHandler : IReadModelStartedMatchesHandler
    {
        private readonly IMatchRepository _matchRepository;

        public MatchReadModelStartedMatchesHandler(
            IMatchRepository matchRepository
            )
        {
            _matchRepository = matchRepository;
        }

        public async Task Update(MatchStartedEvent nextEvent)
        {
            var matchup = OnGoingMatchup.Create(nextEvent);

            foreach (var team in matchup.Teams)
            {
                foreach (var player in team.Players)
                {
                    var foundMatchForPlayer = await _matchRepository.LoadOnGoingMatchForPlayer(player.BattleTag);

                    if (foundMatchForPlayer != null)
                    {
                        await _matchRepository.DeleteOnGoingMatch(foundMatchForPlayer.MatchId);
                    }
                }
            }

            await _matchRepository.InsertOnGoingMatch(matchup);
        }
    }
}