using System;
using System.Threading.Tasks;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;
using Serilog;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Services;

namespace W3ChampionsStatisticService.Matches;

[Trace]
public class MatchReadModelHandler(
    IMatchRepository matchRepository,
    MatchService matchService) : IMatchFinishedReadModelHandler
{
    private readonly IMatchRepository _matchRepository = matchRepository;
    private readonly MatchService _matchService = matchService;

    public async Task Update(MatchFinishedEvent nextEvent)
    {
        try
        {
            if (nextEvent.WasFakeEvent) return;
            var matchup = Matchup.Create(nextEvent);
            await _matchService.SetPlayersCountryCode(matchup);

            await _matchRepository.Insert(matchup);
            await _matchRepository.DeleteOnGoingMatch(matchup);
        }
        catch (Exception e)
        {
            Log.Error($"Error handling MatchFinishedEvent of Match {nextEvent.match.id} for MatchReadModel: {e.Message}");
            throw; // Rethrow or we will lose this event!
        }
    }
}
