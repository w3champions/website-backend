using System;
using System.Threading.Tasks;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;
using Serilog;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Matches;

[Trace]
public class OngoingRemovalMatchFinishedHandler(IMatchRepository matchRepository) : IMatchFinishedReadModelHandler
{
    private readonly IMatchRepository _matchRepository = matchRepository;

    public async Task Update(MatchFinishedEvent nextEvent)
    {
        try
        {
            if (nextEvent.WasFakeEvent) return;
            var matchup = Matchup.Create(nextEvent);

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
