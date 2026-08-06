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
public abstract class MatchEventReadModelHandler<TEvent, THandler>(
    IMatchEventRepository eventRepository,
    IVersionRepository versionRepository,
    THandler innerHandler,
    ITrackingService trackingService) : IAsyncUpdatable
    where TEvent : MatchmakingEvent
    where THandler : class
{
    private readonly IMatchEventRepository _eventRepository = eventRepository;
    private readonly IVersionRepository _versionRepository = versionRepository;
    private readonly THandler _innerHandler = innerHandler;
    private readonly ITrackingService _trackingService = trackingService;

    public async Task Update()
    {
        var lastVersion = await _versionRepository.GetLastVersion<THandler>();
        var events = await _eventRepository.Load<TEvent>(lastVersion.Version, 1000);

        while (events.Count != 0)
        {
            foreach (var matchEvent in events)
            {
                if (lastVersion.IsStopped) return;

                try
                {
                    lastVersion = await ProcessMatchEvent(matchEvent, lastVersion);
                }
                catch (Exception e)
                {
                    Log.Error(e, "Error processing {EventType} {EventId} within the {HandlerType}",
                        typeof(TEvent).Name, matchEvent.Id, typeof(THandler).Name);
                    _trackingService.TrackException(e, $"ReadmodelHandler: {typeof(THandler).Name} died on event {matchEvent.Id}");
                    throw; // rethrow the exception so the event is not lost
                }
            }

            events = await _eventRepository.Load<TEvent>(events.Last().Id.ToString());
        }
    }

    private async Task<HandlerVersion> ProcessMatchEvent(TEvent matchEvent, HandlerVersion lastVersion)
    {
        if (!ShouldProcessEvent(matchEvent))
        {
            // The implementation already logged why. Skipping conditions are permanent (a match never
            // changes its state again), so the watermark still has to advance or the stream stalls here.
            return await AdvancePast(matchEvent, lastVersion);
        }

        lastVersion = await UpdateSeasonIfNeeded(matchEvent, lastVersion);

        await ProcessEventForCurrentSeason(matchEvent, lastVersion);

        return await AdvancePast(matchEvent, lastVersion);
    }

    /// <summary>
    /// Commits the watermark past <paramref name="matchEvent"/> and returns the version the rest of the
    /// batch continues from. Carrying the new version forward matters because
    /// <see cref="UpdateSeasonIfNeeded"/> writes it back verbatim, so a stale value there would
    /// momentarily roll the watermark backwards and re-deliver already handled events after a crash.
    /// </summary>
    private async Task<HandlerVersion> AdvancePast(TEvent matchEvent, HandlerVersion lastVersion)
    {
        await _versionRepository.SaveLastVersion<THandler>(matchEvent.Id.ToString(), lastVersion.Season);
        return new HandlerVersion(matchEvent.Id.ToString(), lastVersion.Season, lastVersion.IsStopped);
    }

    private async Task<HandlerVersion> UpdateSeasonIfNeeded(TEvent matchEvent, HandlerVersion lastVersion)
    {
        var match = GetMatch(matchEvent);
        if (match.season > lastVersion.Season)
        {
            await _versionRepository.SaveLastVersion<THandler>(lastVersion.Version, match.season);
            // Return the updated version from the database
            return await _versionRepository.GetLastVersion<THandler>();
        }

        return lastVersion;
    }

    private async Task ProcessEventForCurrentSeason(TEvent matchEvent, HandlerVersion lastVersion)
    {
        var match = GetMatch(matchEvent);
        if (match.season == lastVersion.Season)
        {
            await UpdateInnerHandler(matchEvent);
        }
        else
        {
            Log.Warning("Old season event {EventSeason} detected during season {CurrentSeason}. Skipping event {EventId}...",
                match.season, lastVersion.Season, matchEvent.Id);
        }
    }

    // Abstract methods that derived classes must implement

    /// <summary>
    /// Decides whether this event is the handler's business at all. Returning false skips the inner
    /// handler and advances the watermark past the event, so implementations must only reject on
    /// permanent conditions and are responsible for logging why they did.
    /// </summary>
    protected abstract bool ShouldProcessEvent(TEvent matchEvent);
    protected abstract Match GetMatch(TEvent matchEvent);
    protected abstract Task UpdateInnerHandler(TEvent matchEvent);
}
