using System.Linq;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;
using W3C.Contracts.Matchmaking;
using Serilog;

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
                    if (nextEvent.match.gameMode != GameMode.CUSTOM)
                    {
                        Log.Information($"Handling Ongoing match: {nextEvent.match.id} for game mode: {nextEvent.match.gameMode}");
                        var matchup = OnGoingMatchup.Create(nextEvent);

                        foreach (var team in matchup.Teams)
                            foreach (var player in team.Players)
                            {
                                var foundMatchForPlayer = await _matchRepository.LoadOnGoingMatchForPlayer(player.BattleTag);
                                if (foundMatchForPlayer != null) {
                                    await _matchRepository.DeleteOnGoingMatch(foundMatchForPlayer.MatchId);
                                    Log.Information($"Deleted Ongoing match: {foundMatchForPlayer.MatchId} because {nextEvent.match.id} was found.");
                                }

                                var personalSettings = await _personalSettingsRepository.Load(player.BattleTag);
                                if (personalSettings != null) {
                                    player.CountryCode = personalSettings.CountryCode;
                                }
                            }

                        await _matchRepository.InsertOnGoingMatch(matchup);
                        Log.Information($"Inserted Ongoing match: {matchup.MatchId}");
                    }
 
                    await _eventRepository.DeleteStartedEvent(nextEvent.Id);
                }

                nextEvents = await _eventRepository.LoadStartedMatches();
            }
        }
    }
}
