using System;
using System.Linq;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.Services;

namespace W3ChampionsStatisticService.ReadModelBase;

public class ReadModelHandler<T> : IAsyncUpdatable where T : IReadModelHandler
{
    private readonly IMatchEventRepository _eventRepository;
    private readonly IVersionRepository _versionRepository;
    private readonly T _innerHandler;
    private readonly ITrackingService _trackingService;
    private readonly ITransactionCoordinator _transactionCoordinator;

    public ReadModelHandler(
        IMatchEventRepository eventRepository,
        IVersionRepository versionRepository,
        T innerHandler,
        ITransactionCoordinator transactionCoordinator,
        ITrackingService trackingService = null)
    {
        _eventRepository = eventRepository;
        _versionRepository = versionRepository;
        _innerHandler = innerHandler;
        _trackingService = trackingService;
        _transactionCoordinator = transactionCoordinator ?? throw new ArgumentNullException(nameof(transactionCoordinator));
    }

    public async Task Update()
    {
        var lastVersion = await _versionRepository.GetLastVersion<T>();
        var nextEvents = await _eventRepository.Load(lastVersion.Version, 1000);

        while (nextEvents.Count != 0)
        {
            foreach (var nextEvent in nextEvents)
            {
                if (lastVersion.IsStopped) return;
                try
                {
                    await using (var transaction = await AsyncTransactionScope.CreateAsync(_transactionCoordinator))
                    {
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
                        transaction.Complete();
                    }
                }
                catch (Exception e)
                {
                    _trackingService?.TrackException(e, $"ReadmodelHandler: {typeof(T).Name} died on event{nextEvent.Id}");
                    throw;
                }
            }

            nextEvents = await _eventRepository.Load(nextEvents.Last().Id.ToString());
        }
    }
}
