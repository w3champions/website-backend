using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Services.BackgroundTasks;

// Periodically publishes progression bracket-population gauges from the PlayerProgression
// read-model for the current ladder season.
public class ProgressionBracketMetricsService(
    ILogger<ProgressionBracketMetricsService> logger,
    IPlayerProgressionRepository playerProgressionRepository,
    IRankRepository rankRepository) : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(15);
    private readonly ILogger<ProgressionBracketMetricsService> _logger = logger;
    private readonly IPlayerProgressionRepository _playerProgressionRepository = playerProgressionRepository;
    private readonly IRankRepository _rankRepository = rankRepository;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var seasons = await _rankRepository.LoadSeasons();
                if (seasons.Count > 0)
                {
                    var season = seasons.Max(s => s.Id);
                    var counts = await _playerProgressionRepository.CountByBracket(season);
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
