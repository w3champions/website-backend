using System;
using System.Threading.Tasks;
using W3C.Domain.MatchmakingService;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.Services;
using W3C.Domain.Tracing;

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

    protected override void ValidateMatchState(MatchFinishedEvent matchEvent)
    {
        if (matchEvent.match.state != EMatchState.FINISHED)
        {
            throw new InvalidOperationException($"Received match with illegal state {matchEvent.match.state} within the MatchFinishedReadModelHandler");
        }
    }

    protected override Match GetMatch(MatchFinishedEvent matchEvent)
    {
        return matchEvent.match;
    }

    protected override async Task UpdateInnerHandler(MatchFinishedEvent matchEvent)
    {
        await _innerHandler.Update(matchEvent);
    }
}
