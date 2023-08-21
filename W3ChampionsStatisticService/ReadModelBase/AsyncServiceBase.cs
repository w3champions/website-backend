using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using W3ChampionsStatisticService.Services;
using Serilog;

namespace W3ChampionsStatisticService.ReadModelBase
{
    public class AsyncServiceBase<T> : IHostedService where T : IAsyncUpdatable
    {
        protected readonly IServiceScopeFactory ServiceScopeFactory;

        private Task _executingTask;
        private readonly CancellationTokenSource _stoppingCts = new CancellationTokenSource();

        public AsyncServiceBase(IServiceScopeFactory serviceScopeFactory)
        {
            ServiceScopeFactory = serviceScopeFactory;
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

        private async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            do
            {
                using (var scope = ServiceScopeFactory.CreateScope())
                {
                    try
                    {
                        var service = scope.ServiceProvider.GetService<T>();
                        await service.Update();
                    }
                    catch (Exception e)
                    {
                        var telemetryClient = scope.ServiceProvider.GetService<TrackingService>();
                        telemetryClient.TrackException(e, "Some Readmodelhandler is dying");
                        Log.Error($"Some Readmodelhandler is dying: {e.Message}");
                    }

                    await Task.Delay(5000, stoppingToken);
                }
            }
            while (!stoppingToken.IsCancellationRequested);
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
    }
}