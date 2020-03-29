using System.Linq;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.ReadModelBase
{
    public class PopulateReadModelHandler<T> where T : IReadModelHandler
    {
        private readonly IMatchEventRepository _eventRepository;
        private readonly IVersionRepository _versionRepository;
        private readonly T _innerHandler;

        public PopulateReadModelHandler(
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
            var lastVersion = await _versionRepository.GetLastVersion<PopulateMatchReadModelHandler>();
            var nextEvents = await _eventRepository.Load(lastVersion, 1000);

            while (nextEvents.Any())
            {
                foreach (var nextEvent in nextEvents)
                {
                    await _innerHandler.Update(nextEvent);
                    await _versionRepository.SaveLastVersion<PopulateMatchReadModelHandler>(nextEvent.Id.ToString());
                }

                nextEvents = await _eventRepository.Load(nextEvents.Last().Id.ToString());
            }
        }
    }
}