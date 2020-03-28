using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.MatchEvents
{
    public class InsertMatchEventsCommandHandler
    {
        private readonly IMatchEventRepository _repository;

        public InsertMatchEventsCommandHandler(IMatchEventRepository repository)
        {
            _repository = repository;
        }

        public Task<string> Insert(IList<MatchFinishedEvent> events)
        {
            return _repository.InsertAsync(events);
        }
    }
}