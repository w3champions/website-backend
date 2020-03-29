using System.Linq;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.ReadModelBase
{
    public class ReadModelHandler<T> where T : IReadModelHandler
    {
        private readonly IMatchEventRepository _eventRepository;
        private readonly IVersionRepository _versionRepository;
        private readonly T _innerHandler;

        public ReadModelHandler(
            IMatchEventRepository eventRepository,
            IVersionRepository versionRepository,
            T innerHandler)
        {
            _eventRepository = eventRepository;
            _versionRepository = versionRepository;
            _innerHandler = innerHandler;
        }

        public async Task Update()
        {
            var lastVersion = await _versionRepository.GetLastVersion<T>();
            var nextEvents = await _eventRepository.Load(lastVersion, 1000);

            while (nextEvents.Any())
            {
                foreach (var nextEvent in nextEvents)
                {
                    await _innerHandler.Update(nextEvent);
                    await _versionRepository.SaveLastVersion<T>(nextEvent.Id.ToString());
                }

                nextEvents = await _eventRepository.Load(nextEvents.Last().Id.ToString());
            }
        }
    }
}