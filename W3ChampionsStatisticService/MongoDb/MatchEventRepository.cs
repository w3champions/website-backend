using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3ChampionsStatisticService.MatchEvents;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.MongoDb
{
    public class MatchEventRepository : IMatchEventRepository
    {
        private readonly DbConnctionInfo _connectionInfo;
        private string databaseName = "test";
        private string _matchfinishedevents = "MatchFinishedEvents";

        public MatchEventRepository(DbConnctionInfo connectionInfo)
        {
            _connectionInfo = connectionInfo;
        }
        public async Task Insert(IEnumerable<MatchFinishedEvent> events)
        {
            var database = CreateClient();

            var mongoCollection = database.GetCollection<MatchFinishedEvent>(_matchfinishedevents);
            await mongoCollection.InsertManyAsync(events);
        }

        public async Task<IEnumerable<MatchFinishedEvent>> Load()
        {
            var database = CreateClient();

            var mongoCollection = database.GetCollection<MatchFinishedEvent>(_matchfinishedevents);

            var events = await mongoCollection
                .Find(ev => true)
                .SortBy(s => s.CreatedDate)
                .ToListAsync();

            return events;
        }

        private IMongoDatabase CreateClient()
        {
            var client = new MongoClient(_connectionInfo.ConnectionString);
            var database = client.GetDatabase(databaseName);
            return database;
        }
    }
}