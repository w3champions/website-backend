using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.ReadModelBase
{
    public class ReadModelStartedMatchesHandler<T> : IAsyncUpdatable where T : IReadModelStartedMatchesHandler
    {
        private readonly IMatchEventRepository _eventRepository;
        private readonly IVersionRepository _versionRepository;
        private readonly T _innerHandler;
        private readonly ILogger<ReadModelStartedMatchesHandler<T>> _logger;

        public ReadModelStartedMatchesHandler(
            IMatchEventRepository eventRepository,
            IVersionRepository versionRepository,
            T innerHandler,
            ILogger<ReadModelStartedMatchesHandler<T>> logger = null)
        {
            _eventRepository = eventRepository;
            _versionRepository = versionRepository;
            _innerHandler = innerHandler;
            _logger = logger ?? new Logger<ReadModelStartedMatchesHandler<T>>(new NullLoggerFactory());
        }

        public async Task Update()
        {
            var lastVersion = await _versionRepository.GetLastVersion<T>();
            var nextEvents = await _eventRepository.LoadStartedMatches(lastVersion.Version, 1000);

            while (nextEvents.Any())
            {
                foreach (var nextEvent in nextEvents)
                {
                    try
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
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, $"ReadmodelHandler: {typeof(T).Name} died on event{nextEvent.Id}");
                        throw;
                    }
                }

                nextEvents = await _eventRepository.LoadStartedMatches(nextEvents.Last().Id.ToString());
            }
        }
    }
}