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
/// One-shot startup backfill of the LagReport lowercased *Search fields onto documents written
/// before those fields existed, so the admin list's case-insensitive prefix search works on
/// historical reports. Runs automatically once: a marker in the HandlerVersions store gates it,
/// so later startups skip it with a single cheap lookup (no collection scan). The underlying
/// UpdateMany is itself idempotent, so a crash before the marker is written just resumes next start.
/// </summary>
public class LagReportSearchBackfillService(
    IServiceScopeFactory scopeFactory,
    ILogger<LagReportSearchBackfillService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var versions = scope.ServiceProvider.GetRequiredService<IVersionRepository>();

            var marker = await versions.GetLastVersion<LagReportSearchBackfillService>();
            if (marker.Version != ObjectId.Empty.ToString())
            {
                return; // Already backfilled.
            }

            var repository = scope.ServiceProvider.GetRequiredService<LagReportRepository>();
            var updated = await repository.BackfillSearchFields(stoppingToken);
            await versions.SaveLastVersion<LagReportSearchBackfillService>(ObjectId.GenerateNewId().ToString());

            logger.LogInformation("LagReport search-field backfill complete: {Count} document(s) updated", updated);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "LagReport search-field backfill failed");
        }
    }
}
