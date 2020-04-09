using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Ladder;
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
            var ranks = events.SelectMany(x =>
                x.ranks.Select((r, i) =>
                    new Rank(x.gateway, x.league, i, r.rp, r.id))).ToList();

            await _repository.Insert(events);
            await _repository.Insert(ranks);
        }
    }
}