using System;
using System.Threading;
using System.Threading.Tasks;
using W3C.Contracts.Admin.Permission;
using W3ChampionsStatisticService.Maps;

namespace W3ChampionsStatisticService.Admin.Jobs;

/// <summary>
/// On-demand run of the daily temporary-map sweep, for operators who need expired files gone now rather than at the
/// next 24 h tick. Idempotent, so re-running after a failure is always safe, and serialised with the daily run by the
/// sweep itself. No checkpoint: a run examines everything it can reach, and an interrupted one is simply run again.
/// </summary>
public class TemporaryMapExpiryJob(TemporaryMapExpirySweep sweep) : IAdminJob
{
    private readonly TemporaryMapExpirySweep _sweep = sweep;

    public string Key => "temporary-maps-expiry";

    public string Name => "Temporary map expiry";

    public string Description =>
        $"Deletes stored files of self-provided custom maps whose last game start is more than " +
        $"{TemporaryMapLimits.TtlDays} days ago, then reclaims CustomGames/ files no map record claims.";

    public EPermission RequiredPermission => EPermission.Maps;

    public async Task RunAsync(IAdminJobContext context, CancellationToken cancellationToken)
    {
        var report = await _sweep.RunOnceAsync(DateTime.UtcNow, cancellationToken);

        var items = report.Scanned + report.Deleted;
        context.AddItems(items);
        await context.Report(
            items,
            0,
            $"scanned={report.Scanned} deleted={report.Deleted} reclaimedOrphans={report.ReclaimedOrphans} " +
            $"purgedSpoolFiles={report.PurgedSpoolFiles} failed={report.Failed}");
    }
}
