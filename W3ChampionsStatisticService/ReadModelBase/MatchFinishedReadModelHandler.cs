using System.Linq;
using System.Threading.Tasks;
using Serilog;
using W3C.Domain.MatchmakingService;
using W3C.Domain.Repositories;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.Services;

namespace W3ChampionsStatisticService.ReadModelBase;

[Trace]
public class MatchFinishedReadModelHandler<T>(
    IMatchEventRepository eventRepository,
    IVersionRepository versionRepository,
    T innerHandler,
    ITrackingService trackingService) : MatchEventReadModelHandler<MatchFinishedEvent, T>(eventRepository, versionRepository, innerHandler, trackingService)
    where T : class, IMatchFinishedReadModelHandler
{
    private readonly T _innerHandler = innerHandler;

    protected override bool ShouldProcessEvent(MatchFinishedEvent matchEvent)
    {
        if (matchEvent.WasFakeEvent || matchEvent.match.state == EMatchState.FINISHED)
        {
            return true;
        }

        if (matchEvent.match.state == EMatchState.CANCELED)
        {
            // A canceled match is simply not this handler's business, and it will never become FINISHED
            // later, so there is nothing to retry. Logged at information level because this is a known
            // and potentially frequent situation that must not drown out the real warnings.
            Log.Information("Skipping canceled match {MatchId} in event {EventId} within the MatchFinishedReadModelHandler",
                matchEvent.match.id, matchEvent.Id);
            return false;
        }

        Log.Warning("Skipping match {MatchId} with illegal state {MatchState} in event {EventId} within the MatchFinishedReadModelHandler",
            matchEvent.match.id, matchEvent.match.state, matchEvent.Id);
        return false;
    }

    protected override Match GetMatch(MatchFinishedEvent matchEvent)
    {
        return matchEvent.match;
    }

    protected override async Task UpdateInnerHandler(MatchFinishedEvent matchEvent)
    {
        StripComputerPlayers(matchEvent);
        await _innerHandler.Update(matchEvent);
    }

    private static void StripComputerPlayers(MatchFinishedEvent matchEvent)
    {
        if (matchEvent.match?.players != null)
        {
            matchEvent.match.players = matchEvent.match.players
                .Where(p => !IsComputer(p))
                .ToList();
        }
        if (matchEvent.result?.players != null)
        {
            matchEvent.result.players = matchEvent.result.players
                .Where(p => !IsComputer(p))
                .ToList();
        }
    }

    private static bool IsComputer(PlayerMMrChange p) => string.IsNullOrEmpty(p?.battleTag);
    private static bool IsComputer(PlayerBlizzard p) => p == null || p.isAi || string.IsNullOrEmpty(p.battleTag);
}
