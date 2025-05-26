using System;
using System.Threading.Tasks;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;
using Serilog;
using W3C.Domain.Tracing;
using W3C.Contracts.Matchmaking;

namespace W3ChampionsStatisticService.Matches;

[Trace]
public class OngoingRemovalMatchCanceledHandler(IMatchRepository matchRepository) : IMatchCanceledReadModelHandler
{
    private readonly IMatchRepository _matchRepository = matchRepository;

    public async Task Update(MatchCanceledEvent nextEvent)
    {
        var ongoingMatch = await _matchRepository.LoadDetailsByOngoingMatchId(nextEvent.match.id);

        if (ongoingMatch == null)
        {
            if (nextEvent.match.gameMode != GameMode.CUSTOM)
            {
                Log.Warning($"Canceled match {nextEvent.match.id} not found");
            }
            return;
        }

        if (ongoingMatch.Match != null)
        {
            Log.Information($"Canceling ongoing match {ongoingMatch.Match.Id}");
            await _matchRepository.DeleteOnGoingMatch(ongoingMatch.Match);
        }
        else
        {
            Log.Warning($"Canceled match detail had null Match property for {nextEvent.match.id}");
            await _matchRepository.DeleteOnGoingMatch(new Matchup { MatchId = nextEvent.match.id });
        }
    }
}
