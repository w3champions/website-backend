using System.Linq;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;
using W3C.Contracts.Matchmaking;

namespace W3ChampionsStatisticService.Matches;

public class OngoingMatchesHandler(
    IMatchEventRepository eventRepository,
    IMatchRepository matchRepository,
    IPersonalSettingsRepository personalSettingsRepository) : IAsyncUpdatable
{
    private readonly IMatchEventRepository _eventRepository = eventRepository;
    private readonly IMatchRepository _matchRepository = matchRepository;
    private readonly IPersonalSettingsRepository _personalSettingsRepository = personalSettingsRepository;

    public async Task Update()
    {
        var nextEvents = await _eventRepository.LoadStartedMatches();

        while (nextEvents.Any())
        {
            foreach (var nextEvent in nextEvents)
            {
                if (nextEvent.match.gameMode != GameMode.CUSTOM)
                {
                    var matchup = OnGoingMatchup.Create(nextEvent);

                    foreach (var team in matchup.Teams)
                        foreach (var player in team.Players)
                        {
                            var foundMatchForPlayer = await _matchRepository.LoadOnGoingMatchForPlayer(player.BattleTag);
                            if (foundMatchForPlayer != null)
                            {
                                await _matchRepository.DeleteOnGoingMatch(foundMatchForPlayer.MatchId);
                            }

                            var personalSettings = await _personalSettingsRepository.LoadOrCreate(player.BattleTag);
                            if (personalSettings != null)
                            {
                                player.CountryCode = personalSettings.CountryCode;
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
