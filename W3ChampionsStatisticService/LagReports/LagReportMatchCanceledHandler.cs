using System.Threading.Tasks;
using W3C.Domain.MatchmakingService;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.LagReports;

/// <summary>
/// When a match is canceled, fetch the flo-stats snapshot: ping data onto an
/// existing lag report, and per-player leave reasons into FloGameLeaveReport.
///
/// Expect a low yield here. Matchmaking only emits MatchCanceledEvent from its
/// 6-hour cleanup sweep, and only for ladder matches, by which point flo has almost
/// certainly evicted the game from its in-memory LRU. The capture is still recorded
/// (with SnapshotAvailable = false) so the miss rate is measurable rather than
/// invisible, but the submission-time and match-finished paths are where data will
/// actually come from.
/// </summary>
[Trace]
public class LagReportMatchCanceledHandler(
    LagReportRepository lagReportRepository,
    FloGameLeaveRepository floGameLeaveRepository,
    IFloStatsService floStatsService
) : IMatchCanceledReadModelHandler
{
    public async Task Update(MatchCanceledEvent nextEvent)
    {
        var floGameId = nextEvent.match.floGameId;
        if (floGameId == null)
        {
            return;
        }

        await floStatsService.FetchAndStoreIfNeeded(
            floGameId.Value, lagReportRepository, floGameLeaveRepository, EFloLeaveCaptureTrigger.MatchCanceled);
    }
}
