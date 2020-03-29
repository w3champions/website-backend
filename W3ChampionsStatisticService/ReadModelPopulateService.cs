using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService
{
    public class ReadModelPopulateService<T> : IHostedService where T : IPopulateReadModelHandlerBase
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;

        private Task _executingTask;
        private readonly CancellationTokenSource _stoppingCts = new CancellationTokenSource();

        public ReadModelPopulateService(IServiceScopeFactory serviceScopeFactory)
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
                        var service = scope.ServiceProvider.GetService<T>();
                        await service.Update();
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }

                    await Task.Delay(5000, stoppingToken);
                }
            }
            while (!stoppingToken.IsCancellationRequested);
        }
    }
}