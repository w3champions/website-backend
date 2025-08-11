using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using W3ChampionsStatisticService.Rewards.Services;

namespace W3ChampionsStatisticService.Rewards.BackgroundServices;

public class RewardDriftDetectionBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RewardDriftDetectionBackgroundService> _logger;
    private readonly int _intervalMinutes;
    private readonly bool _enabled;

    public RewardDriftDetectionBackgroundService(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<RewardDriftDetectionBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        
        // Configure from environment variables
        var intervalStr = Environment.GetEnvironmentVariable("REWARDS_DRIFT_DETECTION_INTERVAL_MINUTES");
        _intervalMinutes = !string.IsNullOrEmpty(intervalStr) && int.TryParse(intervalStr, out var interval) 
            ? interval 
            : 60; // Default 1 hour
            
        var enabledStr = Environment.GetEnvironmentVariable("REWARDS_DRIFT_DETECTION_ENABLED");
        _enabled = enabledStr?.ToLower() == "true"; // Default disabled
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation("Reward drift detection is disabled. Set REWARDS_DRIFT_DETECTION_ENABLED=true to enable.");
            return;
        }

        _logger.LogInformation("Reward drift detection background service started. Will run every {Minutes} minutes.", _intervalMinutes);

        // Initial delay to let the application fully start
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunDriftDetection(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in drift detection background service");
            }

            // Wait for the configured interval
            await Task.Delay(TimeSpan.FromMinutes(_intervalMinutes), stoppingToken);
        }
    }

    private async Task RunDriftDetection(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting scheduled drift detection run");

        using var scope = _serviceProvider.CreateScope();
        
        try
        {
            // Run Patreon drift detection
            var patreonDriftService = scope.ServiceProvider.GetService<PatreonDriftDetectionService>();
            if (patreonDriftService != null)
            {
                _logger.LogInformation("Running Patreon drift detection");
                var patreonResult = await patreonDriftService.DetectDrift();
                
                if (patreonResult.HasDrift)
                {
                    _logger.LogWarning("Patreon drift detected during scheduled run. Missing: {Missing}, Extra: {Extra}, Mismatched: {Mismatched}",
                        patreonResult.MissingMembers.Count, 
                        patreonResult.ExtraAssignments.Count, 
                        patreonResult.MismatchedTiers.Count);
                }
            }
            else
            {
                _logger.LogDebug("Patreon drift detection service not available");
            }

            // Future: Add Ko-Fi drift detection here when implemented
            // var kofiDriftService = scope.ServiceProvider.GetService<KoFiDriftDetectionService>();
            // if (kofiDriftService != null)
            // {
            //     _logger.LogInformation("Running Ko-Fi drift detection");
            //     var kofiResult = await kofiDriftService.DetectDrift();
            //     ...
            // }

            _logger.LogInformation("Scheduled drift detection run completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during scheduled drift detection");
        }
    }
}