using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.MongoDb
{
    public class MatchRepository : IMatchRepository
    {
        public Task Upsert(IList<Matchup> matchups)
        {
            return Task.CompletedTask;
        }
    }
}