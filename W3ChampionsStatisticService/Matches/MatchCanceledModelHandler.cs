using System.Linq;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;
using W3ChampionsStatisticService.Services;
using W3C.Contracts.Matchmaking;
using Serilog;
using System.Transactions;
using System;
using W3C.Domain.MatchmakingService;

namespace W3ChampionsStatisticService.Matches;

public class MatchCanceledModelHandler(IMatchRepository matchRepository) : IMatchCanceledReadModelHandler
{
    private readonly IMatchRepository _matchRepository = matchRepository;


    public async Task Update(MatchCanceledEvent nextEvent)
    {
        if (nextEvent.match.gameMode == GameMode.CUSTOM) return;

        var ongoingMatch = await _matchRepository.LoadDetailsByOngoingMatchId(nextEvent.match.id);

        if (ongoingMatch == null)
        {
            Log.Warning($"Canceled match {nextEvent.match.id} not found");
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
