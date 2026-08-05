using System.Threading.Tasks;
using Serilog;
using W3C.Domain.MatchmakingService;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.Services;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.ReadModelBase;

[Trace]
public class MatchCanceledReadModelHandler<T>(
    IMatchEventRepository eventRepository,
    IVersionRepository versionRepository,
    T innerHandler,
    ITrackingService trackingService) : MatchEventReadModelHandler<MatchCanceledEvent, T>(eventRepository, versionRepository, innerHandler, trackingService)
    where T : class, IMatchCanceledReadModelHandler
{
    private readonly T _innerHandler = innerHandler;

    protected override bool ShouldProcessEvent(MatchCanceledEvent matchEvent)
    {
        if (matchEvent.match.state == EMatchState.CANCELED)
        {
            return true;
        }

        // A match never changes its state again, so this event will never become processable - retrying
        // it would wedge the handler forever. Skip it and let the watermark move on.
        Log.Warning("Skipping match {MatchId} with illegal state {MatchState} in event {EventId} within the MatchCanceledReadModelHandler",
            matchEvent.match.id, matchEvent.match.state, matchEvent.Id);
        return false;
    }

    protected override Match GetMatch(MatchCanceledEvent matchEvent)
    {
        return matchEvent.match;
    }

    protected override async Task UpdateInnerHandler(MatchCanceledEvent matchEvent)
    {
        await _innerHandler.Update(matchEvent);
    }
}
