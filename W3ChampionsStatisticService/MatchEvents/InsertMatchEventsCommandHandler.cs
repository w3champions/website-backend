using System;
using System.Collections.Generic;
using System.Linq;
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

        public Task Insert(IList<MatchFinishedEvent> events)
        {
            foreach (var matchFinishedEvent in events)
            {
                matchFinishedEvent.Id = Guid.NewGuid();
                matchFinishedEvent.CreatedDate = DateTimeOffset.UtcNow;
            }

            return _repository.Insert(events);
        }
    }
}