using System;
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

        public async Task<List<Matchup>> Load(DateTimeOffset since = default, int pageSize = 100)
        {
            var database = CreateClient();

            var mongoCollection = database.GetCollection<Matchup>(nameof(Matchup));
            var filterBuilder = Builders<Matchup>.Filter;
            var filter = filterBuilder.Gt(x => x.StartTime, since);

            var events = await mongoCollection.Find(filter)
                .SortBy(s => s.StartTime)
                .Limit(pageSize)
                .ToListAsync();

            return events;
        }
    }
}