using System;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using W3C.Domain.MatchmakingService;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.Services;

namespace W3ChampionsStatisticService.ReadModelBase;

public class MatchFinishedReadModelHandler<T>(
    IMatchEventRepository eventRepository,
    IVersionRepository versionRepository,
    T innerHandler,
    ITrackingService trackingService = null) : IAsyncUpdatable where T : IMatchFinishedReadModelHandler
    {
        private readonly IMatchEventRepository _eventRepository = eventRepository;
        private readonly IVersionRepository _versionRepository = versionRepository;
        private readonly T _innerHandler = innerHandler;
        private readonly ITrackingService _trackingService = trackingService;
    }
    public async Task Update()
    {
        var lastVersion = await _versionRepository.GetLastVersion<T>();
        var nextEvents = await _eventRepository.Load<MatchFinishedEvent>(lastVersion.Version, 1000);

        while (nextEvents.Count != 0)
        {
            foreach (var nextEvent in nextEvents)
            {
                if (lastVersion.IsStopped) return;
                try
                {
                    if (nextEvent.match.season > lastVersion.Season)
                    {
                        await _versionRepository.SaveLastVersion<T>(lastVersion.Version, nextEvent.match.season);
                        lastVersion = await _versionRepository.GetLastVersion<T>();
                    }

                    if (nextEvent.match.season == lastVersion.Season)
                    {
                        await _innerHandler.Update(nextEvent);
                    }
                    else 
                    {
                        Log.Error($"Old season event {nextEvent.match.season} detected during season {lastVersion.Season}. Skipping event...");
                    }

                    await _versionRepository.SaveLastVersion<T>(nextEvent.Id.ToString(), lastVersion.Season);
                }
                catch (Exception e)
                {
                    _trackingService?.TrackException(e, $"ReadmodelHandler: {typeof(T).Name} died on event{nextEvent.Id}");
                    throw;
                }
            }

            nextEvents = await _eventRepository.Load<MatchFinishedEvent>(nextEvents.Last().Id.ToString());
        }
    }
}
