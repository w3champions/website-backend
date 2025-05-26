using System;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using W3C.Domain.MatchmakingService;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.Services;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.ReadModelBase;

[Trace]
public class MatchFinishedReadModelHandler<T>(
    IMatchEventRepository eventRepository,
    IVersionRepository versionRepository,
    T innerHandler,
    ITrackingService trackingService) : IAsyncUpdatable where T : IMatchFinishedReadModelHandler
{
    private readonly IMatchEventRepository _eventRepository = eventRepository;
    private readonly IVersionRepository _versionRepository = versionRepository;
    private readonly T _innerHandler = innerHandler;
    private readonly ITrackingService _trackingService = trackingService;

    public async Task Update()
    {
        var lastVersion = await _versionRepository.GetLastVersion<T>();
        var finishedEvents = await _eventRepository.Load<MatchFinishedEvent>(lastVersion.Version, 1000);

        while (finishedEvents.Count != 0)
        {
            foreach (var matchEvent in finishedEvents)
            {
                if (lastVersion.IsStopped) return;

                try
                {
                    lastVersion = await ProcessMatchFinishedEvent(matchEvent, lastVersion);
                }
                catch (Exception e)
                {
                    Log.Error(e, "Error processing MatchFinishedEvent {EventId} within the MatchFinishedReadModelHandler", matchEvent.Id);
                    _trackingService.TrackException(e, $"ReadmodelHandler: {typeof(T).Name} died on event {matchEvent.Id}");
                    throw; // rethrow the exception so the event is not lost
                }
            }

            finishedEvents = await _eventRepository.Load<MatchFinishedEvent>(finishedEvents.Last().Id.ToString());
        }
    }

    private async Task<HandlerVersion> ProcessMatchFinishedEvent(MatchFinishedEvent matchEvent, HandlerVersion lastVersion)
    {
        ValidateMatchState(matchEvent);

        lastVersion = await UpdateSeasonIfNeeded(matchEvent, lastVersion);

        await ProcessEventForCurrentSeason(matchEvent, lastVersion);

        await _versionRepository.SaveLastVersion<T>(matchEvent.Id.ToString(), lastVersion.Season);
        return lastVersion;
    }

    private static void ValidateMatchState(MatchFinishedEvent matchEvent)
    {
        if (matchEvent.match.state != EMatchState.FINISHED)
        {
            throw new InvalidOperationException($"Received match with illegal state {matchEvent.match.state} within the MatchFinishedReadModelHandler");
        }
    }

    private async Task<HandlerVersion> UpdateSeasonIfNeeded(MatchFinishedEvent matchEvent, HandlerVersion lastVersion)
    {
        if (matchEvent.match.season > lastVersion.Season)
        {
            await _versionRepository.SaveLastVersion<T>(lastVersion.Version, matchEvent.match.season);
            // Return the updated version from the database
            return await _versionRepository.GetLastVersion<T>();
        }

        return lastVersion;
    }

    private async Task ProcessEventForCurrentSeason(MatchFinishedEvent matchEvent, HandlerVersion lastVersion)
    {
        if (matchEvent.match.season == lastVersion.Season)
        {
            await _innerHandler.Update(matchEvent);
        }
        else
        {
            Log.Warning("Old season event {EventSeason} detected during season {CurrentSeason}. Skipping event {EventId}...",
                matchEvent.match.season, lastVersion.Season, matchEvent.Id);
        }
    }
}
