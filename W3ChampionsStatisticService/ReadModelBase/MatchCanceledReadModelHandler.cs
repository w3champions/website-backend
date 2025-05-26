using System;
using System.Threading.Tasks;
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

    protected override void ValidateMatchState(MatchCanceledEvent matchEvent)
    {
        if (matchEvent.match.state != EMatchState.CANCELED)
        {
            throw new InvalidOperationException($"Received match with illegal state {matchEvent.match.state} within the MatchCanceledReadModelHandler");
        }
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
