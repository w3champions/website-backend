using System.Threading.Tasks;
using W3C.Domain.MatchmakingService;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.LagReports;

/// <summary>
/// When a match is canceled, check if a lag report exists for that game
/// and fetch server-side ping data from flo-stats if needed, then refresh the players'
/// relay chains whose connections were still open when they submitted.
/// Canceled games (e.g. disconnects) are often the most interesting for diagnostics.
/// </summary>
[Trace]
public class LagReportMatchCanceledHandler(
    LagReportRepository lagReportRepository,
    IFloStatsService floStatsService,
    IRelayTelemetryService relayTelemetryService
) : IMatchCanceledReadModelHandler
{
    public async Task Update(MatchCanceledEvent nextEvent)
    {
        var floGameId = nextEvent.match.floGameId;
        if (floGameId == null)
        {
            return;
        }

        await floStatsService.FetchAndStoreIfNeeded(floGameId.Value, lagReportRepository);
        await relayTelemetryService.RefreshOpen(floGameId.Value);
    }
}
