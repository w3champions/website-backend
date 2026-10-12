using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace W3ChampionsStatisticService.NetworkDiagnostics.Delivery;

/// <summary>Runs one scheduler tick every 5 s. A failing tick is logged; the loop continues.</summary>
public sealed class NetworkDiagnosticsDispatcher(
    INetworkDiagnosticsScheduler scheduler,
    TimeProvider time,
    ILogger<NetworkDiagnosticsDispatcher> log,
    TimeSpan? interval = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval ?? NetDiagConstants.DispatchInterval, time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await scheduler.TickAsync(stoppingToken);
                }
                catch (Exception e) when (!stoppingToken.IsCancellationRequested)
                {
                    log.LogError(e, "Network diagnostics dispatcher tick failed");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown.
        }
    }
}
