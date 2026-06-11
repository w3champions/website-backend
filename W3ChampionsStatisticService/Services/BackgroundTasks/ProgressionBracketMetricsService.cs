using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Services.BackgroundTasks;

// Periodically publishes progression bracket-population gauges from the PlayerProgression
// read-model (current season = highest season present). Registered only when
// PROGRESSION_METRICS_ENABLED=true (Program.cs).
public class ProgressionBracketMetricsService(
    ILogger<ProgressionBracketMetricsService> logger,
    IPlayerProgressionRepository playerProgressionRepository) : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(15);
    private readonly ILogger<ProgressionBracketMetricsService> _logger = logger;
    private readonly IPlayerProgressionRepository _playerProgressionRepository = playerProgressionRepository;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var season = await _playerProgressionRepository.LoadMaxSeason();
                if (season != null)
                {
                    var counts = await _playerProgressionRepository.CountByBracket(season.Value);
                    ProgressionBracketMetrics.Publish(counts);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing progression bracket metrics");
            }
            await Task.Delay(RefreshInterval, stoppingToken);
        }
    }
}
