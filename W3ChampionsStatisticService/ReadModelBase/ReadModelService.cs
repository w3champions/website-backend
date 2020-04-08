using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.Services;

namespace W3ChampionsStatisticService.ReadModelBase
{
    public class ReadModelService<T> : IHostedService where T : IReadModelHandler
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;

        private Task _executingTask;
        private readonly CancellationTokenSource _stoppingCts = new CancellationTokenSource();

        public ReadModelService(IServiceScopeFactory serviceScopeFactory)
        {
            _serviceScopeFactory = serviceScopeFactory;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _executingTask = ExecuteAsync(_stoppingCts.Token);

            if (_executingTask.IsCompleted)
            {
                return _executingTask;
            }

            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_executingTask == null)
            {
                return;
            }

            try
            {
                _stoppingCts.Cancel();
            }
            finally
            {
                await Task.WhenAny(_executingTask, Task.Delay(Timeout.Infinite, cancellationToken));
            }
        }

        private async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            do
            {
                using (var scope = _serviceScopeFactory.CreateScope())
                {
                    try
                    {
                        var service = scope.ServiceProvider.GetService<ReadModelHandler<T>>();
                        await service.Update();
                    }
                    catch (Exception e)
                    {
                        var telemetryClient = scope.ServiceProvider.GetService<TrackingService>();
                        telemetryClient.TrackException(e);
                    }

                    await Task.Delay(5000, stoppingToken);
                }
            }
            while (!stoppingToken.IsCancellationRequested);
        }
    }
}