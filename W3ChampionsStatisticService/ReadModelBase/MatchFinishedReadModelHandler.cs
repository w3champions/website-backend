using System;
using System.Linq;
using System.Threading.Tasks;
using W3C.Contracts.Matchmaking;
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

    protected override void ValidateMatchState(MatchFinishedEvent matchEvent)
    {
        if (!matchEvent.WasFakeEvent && matchEvent.match.state != EMatchState.FINISHED)
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
