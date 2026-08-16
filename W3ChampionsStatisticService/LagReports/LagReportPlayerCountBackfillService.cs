using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.LagReports;

/// <summary>
/// One-shot startup backfill of LagReport.PlayerCount onto documents written before the field
/// existed, so the min/maxPlayers filters cover historical reports. Same shape as
/// <see cref="LagReportSearchBackfillService"/>: a marker in the HandlerVersions store gates it
/// to one run, and the underlying UpdateMany is itself idempotent, so a crash before the marker
/// is written just resumes next start.
/// </summary>
public class LagReportPlayerCountBackfillService(
    IServiceScopeFactory scopeFactory,
    ILogger<LagReportPlayerCountBackfillService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var versions = scope.ServiceProvider.GetRequiredService<IVersionRepository>();

            var marker = await versions.GetLastVersion<LagReportPlayerCountBackfillService>();
            if (marker.Version != ObjectId.Empty.ToString())
            {
                return; // Already backfilled.
            }

            var repository = scope.ServiceProvider.GetRequiredService<LagReportRepository>();
            var updated = await repository.BackfillPlayerCounts(stoppingToken);
            await versions.SaveLastVersion<LagReportPlayerCountBackfillService>(ObjectId.GenerateNewId().ToString());

            logger.LogInformation("LagReport player-count backfill complete: {Count} document(s) updated", updated);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "LagReport player-count backfill failed");
        }
    }
}
