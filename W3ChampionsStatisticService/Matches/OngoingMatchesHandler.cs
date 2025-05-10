using System.Linq;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;
using W3C.Contracts.Matchmaking;
using Serilog;
using W3ChampionsStatisticService.Services;
using System;

namespace W3ChampionsStatisticService.Matches;

public class OngoingMatchesHandler(
    IMatchEventRepository eventRepository,
    IMatchRepository matchRepository,
    IPersonalSettingsRepository personalSettingsRepository,
    ITrackingService trackingService) : IAsyncUpdatable
{
    private readonly IMatchEventRepository _eventRepository = eventRepository;
    private readonly IMatchRepository _matchRepository = matchRepository;
    private readonly IPersonalSettingsRepository _personalSettingsRepository = personalSettingsRepository;
    private readonly ITrackingService _trackingService = trackingService;

    public async Task Update()
    {
        var nextEvents = await _eventRepository.LoadStartedMatches();

        while (nextEvents.Count != 0)
        {
            foreach (var nextEvent in nextEvents)
            {
                try
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
                                    Log.Warning("Deleting stale ongoing match {MatchId} for player {Player} as new match {NewMatchId} is starting", foundMatchForPlayer.MatchId, player.BattleTag, nextEvent.match.id);
                                    await _matchRepository.DeleteOnGoingMatch(foundMatchForPlayer);
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
                catch (Exception e)
                {
                    _trackingService?.TrackException(e, $"OngoingMatchesHandler died on event {nextEvent.Id}");
                    throw;
                }
            }

            nextEvents = await _eventRepository.LoadStartedMatches();
        }
    }
}
