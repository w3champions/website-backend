using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using W3ChampionsStatisticService.Maps;

namespace W3ChampionsStatisticService.Services.BackgroundTasks;

/// <summary>
/// The daily trigger for <see cref="TemporaryMapExpirySweep"/>, shaped like <see cref="UpdateMaxMmrService"/>: one sweep
/// when the host starts (which also purges the spool files a crash or restart left behind), then one every
/// <see cref="TemporaryMapLimits.SweepIntervalHours"/>. The AdminJobRunner is deliberately a runner and not a scheduler
/// (docs/admin-job-runner.md), so the recurring trigger lives here; the same sweep is also exposed as the
/// "temporary-maps-expiry" admin job for on-demand runs, and the sweep's own lock keeps the two from overlapping.
/// </summary>
public class TemporaryMapExpiryService(
    TemporaryMapExpirySweep sweep,
    ILogger<TemporaryMapExpiryService> logger) : BackgroundService
{
    private readonly TemporaryMapExpirySweep _sweep = sweep;
    private readonly ILogger<TemporaryMapExpiryService> _logger = logger;

    /// <summary>Test seam: the wait between sweeps.</summary>
    internal TimeSpan Interval { get; init; } = TimeSpan.FromHours(TemporaryMapLimits.SweepIntervalHours);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _sweep.RunOnceAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // The sweep isolates every failure itself; this is for a defect in it. Left unhandled, an exception here
                // stops the whole host (BackgroundServiceExceptionBehavior.StopHost), so it is logged and the loop goes on.
                _logger.LogError(ex, "Temporary map expiry sweep failed; the next one runs at the usual time");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
