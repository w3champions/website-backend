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
        private readonly IPersonalSettingsRepository _personalSettingsRepository;

        public OngoingMatchesHandler(
            IMatchEventRepository eventRepository,
            IMatchRepository matchRepository,
            IPersonalSettingsRepository personalSettingsRepository)
        {
            _eventRepository = eventRepository;
            _matchRepository = matchRepository;
            _personalSettingsRepository = personalSettingsRepository;
        }

        public async Task Update()
        {
            var nextEvents = await _eventRepository.LoadStartedMatches();

            while (nextEvents.Any())
            {
                foreach (var nextEvent in nextEvents)
                {
                    if (nextEvent.match.gameMode != CommonValueObjects.GameMode.CUSTOM)
                    {
                        var matchup = OnGoingMatchup.Create(nextEvent);

                        foreach (var team in matchup.Teams)
                            foreach (var player in team.Players)
                            {
                                var foundMatchForPlayer = await _matchRepository.LoadOnGoingMatchForPlayer(player.BattleTag);
                                if (foundMatchForPlayer != null)
                                    await _matchRepository.DeleteOnGoingMatch(foundMatchForPlayer.MatchId);

                                // Use the players personal settings to update their location information
                                var personalSettings = await _personalSettingsRepository.Load(player.BattleTag);
                                if (personalSettings != null)
                                {
                                    player.CountryCode = personalSettings.CountryCode;
                                    player.Location = personalSettings.Location;
                                    player.Country = personalSettings.Country;
                                }
                            }

                        await _matchRepository.InsertOnGoingMatch(matchup);
                    }
 
                    await _eventRepository.DeleteStartedEvent(nextEvent.Id);
                }

                nextEvents = await _eventRepository.LoadStartedMatches();
            }
        }
    }
}