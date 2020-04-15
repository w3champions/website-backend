using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.PadEvents
{
    public class InsertMatchEventsCommandHandler
    {
        private readonly IMatchEventRepository _repository;

        public InsertMatchEventsCommandHandler(IMatchEventRepository repository)
        {
            _repository = repository;
        }

        public Task Insert(List<MatchFinishedEvent> events)
        {
            return _repository.Insert(events);
        }

        public Task Insert(List<MatchStartedEvent> events)
        {
            return _repository.Insert(events);
        }

        public Task Insert(List<LeagueConstellationChangedEvent> events)
        {
            return _repository.Insert(events);
        }

        public async Task Insert(List<RankingChangedEvent> events)
        {
            await _repository.Insert(events);

        }

        public async Task Insert(List<MatchCanceledEvent> events)
        {
            await _repository.Insert(events);
        }
    }
}