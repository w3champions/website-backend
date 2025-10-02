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
        var ongoingMatch = await _matchRepository.LoadOnGoingMatchByMatchId(nextEvent.match.id);

        if (ongoingMatch == null)
        {
            if (nextEvent.match.gameMode != GameMode.CUSTOM)
            {
                Log.Warning($"Canceled match {nextEvent.match.id} not found");
            }
            return;
        }

        Log.Information($"Canceling ongoing match {ongoingMatch.MatchId} ({ongoingMatch.Id})");
        await _matchRepository.DeleteOnGoingMatch(ongoingMatch);
    }
}
