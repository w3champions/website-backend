﻿using System;
using System.Linq;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.Services;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.ReadModelBase;

[Trace]
public class ReadModelHandler<T>(
    IMatchEventRepository eventRepository,
    IVersionRepository versionRepository,
    T innerHandler,
    TrackingService trackingService = null) : IAsyncUpdatable where T : IReadModelHandler
{
    private readonly IMatchEventRepository _eventRepository = eventRepository;
    private readonly IVersionRepository _versionRepository = versionRepository;
    private readonly T _innerHandler = innerHandler;
    private readonly TrackingService _trackingService = trackingService;

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
}
