using System.Threading.Tasks;
using W3C.Domain.MatchmakingService;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.LagReports;

/// <summary>
/// When a match finishes, check if a lag report exists for that game
/// and fetch server-side ping data from flo-stats if needed.
/// </summary>
[Trace]
public class LagReportMatchFinishedHandler(
    LagReportRepository lagReportRepository,
    IFloStatsService floStatsService
) : IMatchFinishedReadModelHandler
{
    public async Task Update(MatchFinishedEvent nextEvent)
    {
        var floGameId = nextEvent.match.floGameId;
        if (floGameId == null)
        {
            return;
        }

        await floStatsService.FetchAndStoreIfNeeded(floGameId.Value, lagReportRepository);
    }
}
