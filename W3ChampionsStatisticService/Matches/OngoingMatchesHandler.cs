using System.Linq;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Matches
{
    public class OngoingMatchesHandler : IAsyncUpdatable
    {
        private readonly IMatchEventRepository _eventRepository;
        private readonly IMatchRepository _matchRepository;

        public OngoingMatchesHandler(
            IMatchEventRepository eventRepository,
            IMatchRepository matchRepository)
        {
            _eventRepository = eventRepository;
            _matchRepository = matchRepository;
        }

        public async Task Update()
        {
            var nextEvents = await _eventRepository.LoadStartedMatches();

            while (nextEvents.Any())
            {
                foreach (var nextEvent in nextEvents)
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
                    await _eventRepository.DeleteStartedEvent(nextEvent.Id);
                }

                nextEvents = await _eventRepository.LoadStartedMatches();
            }
        }
    }
}