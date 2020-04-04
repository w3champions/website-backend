using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.ReadModelBase
{
    public class ReadModelHandler<T> where T : IReadModelHandler
    {
        private readonly IMatchEventRepository _eventRepository;
        private readonly IVersionRepository _versionRepository;
        private readonly T _innerHandler;
        private readonly ILogger<ReadModelHandler<T>> _logger;

        public ReadModelHandler(
            IMatchEventRepository eventRepository,
            IVersionRepository versionRepository,
            T innerHandler,
            ILogger<ReadModelHandler<T>> logger = null)
        {
            _eventRepository = eventRepository;
            _versionRepository = versionRepository;
            _innerHandler = innerHandler;
            _logger = logger ?? new Logger<ReadModelHandler<T>>(new NullLoggerFactory());
        }

        public async Task Update()
        {
            var lastVersion = await _versionRepository.GetLastVersion<T>();
            var nextEvents = await _eventRepository.Load(lastVersion, 1000);

            while (nextEvents.Any())
            {
                foreach (var nextEvent in nextEvents)
                {
                    try
                    {
                        await _innerHandler.Update(nextEvent);
                        await _versionRepository.SaveLastVersion<T>(nextEvent.Id.ToString());
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, $"ReadmodelHandler: {typeof(T).Name} died on event{nextEvent.Id}");
                        throw;
                    }
                }

                nextEvents = await _eventRepository.Load(nextEvents.Last().Id.ToString());
            }
        }
    }
}