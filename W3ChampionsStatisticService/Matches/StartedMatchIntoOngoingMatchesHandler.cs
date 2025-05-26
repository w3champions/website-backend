using System;
using System.Linq;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;
using W3C.Contracts.Matchmaking;
using W3C.Domain.Tracing;
using Serilog;
using W3ChampionsStatisticService.Services;
using W3C.Domain.MatchmakingService;

namespace W3ChampionsStatisticService.Matches;

[Trace]
public class StartedMatchIntoOngoingMatchesHandler(
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
        var startedEvents = await _eventRepository.LoadStartedMatches();

        while (startedEvents.Count != 0)
        {
            foreach (var matchEvent in startedEvents)
            {
                try
                {
                    await ProcessMatchStartedEvent(matchEvent);
                    await _eventRepository.DeleteStartedEvent(matchEvent.Id);
                }
                catch (Exception e)
                {
                    Log.Error(e, "Error handling MatchStartedEvent {EventId} for OngoingMatchesHandler", matchEvent.Id);
                    _trackingService.TrackException(e, $"OngoingMatchesHandler died on event {matchEvent.Id}");
                    throw; // rethrow the exception so the event is not lost
                }
            }

            startedEvents = await _eventRepository.LoadStartedMatches();
        }
    }

    private async Task ProcessMatchStartedEvent(MatchStartedEvent matchEvent)
    {
        if (matchEvent.match.gameMode == GameMode.CUSTOM)
        {
            Log.Information("Received MatchStartedEvent {EventId} for Custom game {GameId} - not adding to ongoing matches", matchEvent.Id, matchEvent.match.id);
            return;
        }
        var matchup = OnGoingMatchup.Create(matchEvent);
        await ProcessPlayersInMatchup(matchup, matchEvent);
        await _matchRepository.InsertOnGoingMatch(matchup);
    }

    private async Task ProcessPlayersInMatchup(OnGoingMatchup matchup, MatchStartedEvent matchEvent)
    {
        foreach (var player in matchup.Teams.SelectMany(team => team.Players))
        {
            await HandleExistingOngoingMatch(player, matchEvent);
            await SetPlayerCountryCode(player);
        }
    }

    private async Task HandleExistingOngoingMatch(PlayerOverviewMatches player, MatchStartedEvent matchEvent)
    {
        var existingMatch = await _matchRepository.LoadOnGoingMatchForPlayer(player.BattleTag);
        if (existingMatch != null)
        {
            Log.Warning("Received Players {Player} MatchStartedEvent {EventId} for match {MatchId} although we still have an ongoing match {OngoingMatchId} - deleting old ongoing match...",
                player.BattleTag, matchEvent.Id, matchEvent.match.id, existingMatch.MatchId);
            await _matchRepository.DeleteOnGoingMatch(existingMatch);
        }
    }

    private async Task SetPlayerCountryCode(PlayerOverviewMatches player)
    {
        var personalSettings = await _personalSettingsRepository.LoadOrCreate(player.BattleTag);
        player.CountryCode = personalSettings.CountryCode;
    }
}
