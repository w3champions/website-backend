using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.Services;

namespace W3ChampionsStatisticService.ReadModelBase
{
    public class ReadModelHandler<T> : IAsyncUpdatable where T : IReadModelHandler
    {
        private readonly IMatchEventRepository _eventRepository;
        private readonly IVersionRepository _versionRepository;
        private readonly T _innerHandler;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly TrackingService _trackingService;

        public ReadModelHandler(
            IMatchEventRepository eventRepository,
            IVersionRepository versionRepository,
            T innerHandler,
            IServiceScopeFactory serviceScopeFactory,
            TrackingService trackingService = null)
        {
            _eventRepository = eventRepository;
            _versionRepository = versionRepository;
            _innerHandler = innerHandler;
            _serviceScopeFactory = serviceScopeFactory;
            _trackingService = trackingService;
        }

        public async Task Update()
        {
            var lastVersion = await _versionRepository.GetLastVersion<T>();
            var nextEvents = await _eventRepository.Load(lastVersion.Version, 1000);

            while (nextEvents.Any())
            {
                foreach (var nextEvent in nextEvents)
                {
                    try
                    {
                        if (lastVersion.IsStopped) return;
                        if (lastVersion.SyncState == SyncState.SyncStartRequested)
                        {
                            await StartParallelThread();
                        }

                        if (nextEvent.match.season > lastVersion.Season)
                        {
                            await _versionRepository.SaveLastVersion<T>(lastVersion.Version, nextEvent.match.season);
                            lastVersion = await _versionRepository.GetLastVersion<T>();
                        }

                        // Skip the cancel events for now
                        if (nextEvent.match.state != 3 && nextEvent.match.season == lastVersion.Season)
                        {
                            await _innerHandler.Update(nextEvent);
                        }

                        await _versionRepository.SaveLastVersion<T>(nextEvent.Id.ToString(), lastVersion.Season);
                    }
                    catch (Exception e)
                    {
                        _trackingService.TrackException(e, $"ReadmodelHandler: {typeof(T).Name} died on event{nextEvent.Id}");
                        throw;
                    }
                }

                nextEvents = await _eventRepository.Load(nextEvents.Last().Id.ToString());
            }
        }

        private async Task StartParallelThread()
        {
            var serviceScope = _serviceScopeFactory.CreateScope();
            var readModelHandler = serviceScope.ServiceProvider.GetService<ReadModelHandler<T>>();
            readModelHandler.SetAsTempRepoPrefix();

            await _versionRepository.SaveSyncState<T>(SyncState.ParallelSyncStarted);

            Task.Run(() => readModelHandler.Update());

            await _versionRepository.SaveSyncState<T>(SyncState.ParallelSyncStarted);

        }

        private void SetAsTempRepoPrefix()
        {
            _innerHandler.SetAsTempRepoPrefix();
        }
    }
}