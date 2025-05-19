using System;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Services;

public class MatchupHeroBackfillService(IMatchRepository matchRepository) : IAsyncUpdatable
{
    private readonly IMatchRepository matchRepository = matchRepository;
    private DateTimeOffset? startTime = DateTimeOffset.Now;

    public async Task Update()
    {
        try
        {
            if (!startTime.HasValue)
            {
                return;
            }

            DateTimeOffset? nextStartTime = await matchRepository.AddPlayerHeroes(startTime.Value, 1000);
            if (nextStartTime == null)
            {
                // Nothing else to update
                startTime = null;
            }
            else if (nextStartTime.Value >= startTime.Value)
            {
                Log.Warning("Matchup Update returned the same or more recent time. Changing time manually to prevent infinite loop.");
                startTime = startTime.Value.AddTicks(-1);
            }
            else
            {
                startTime = nextStartTime;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error when attempting to update Matchup Player Heroes");
        }
    }
}
