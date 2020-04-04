using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Matches
{
    public class MatchRepository : MongoDbRepositoryBase, IMatchRepository
    {
        public MatchRepository(DbConnctionInfo connectionInfo) : base(connectionInfo)
        {
        }

        public Task Insert(Matchup matchup)
        {
            return Upsert(matchup, m => m.Id == matchup.Id);
        }

        public async Task<List<Matchup>> Load(int offset = default, int pageSize = 100, int gateWay = 10)
        {
            var database = CreateClient();

            var mongoCollection = database.GetCollection<Matchup>(nameof(Matchup));

            var events = await mongoCollection.Find(m => m.GateWay == gateWay)
                .SortBy(s => s.StartTime)
                .Skip(offset)
                .Limit(pageSize)
                .ToListAsync();

            return events;
        }
    }
}