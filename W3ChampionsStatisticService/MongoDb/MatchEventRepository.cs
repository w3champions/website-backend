using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3ChampionsStatisticService.MatchEvents;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.MongoDb
{
    public class MatchEventRepository : IMatchEventRepository
    {
        private readonly DbConnctionInfo _connectionInfo;
        private string databaseName = "W3Champions-Statistic-Service";
        private string _matchfinishedevents = "MatchFinishedEvents";

        public MatchEventRepository(DbConnctionInfo connectionInfo)
        {
            _connectionInfo = connectionInfo;
        }
        public async Task InsertAsync(IList<MatchFinishedEvent> events)
        {
            if (!events.Any()) return;
            var database = CreateClient();

            var mongoCollection = database.GetCollection<MatchFinishedEvent>(_matchfinishedevents);
            await mongoCollection.InsertManyAsync(events);
        }

        public async Task<IList<MatchFinishedEvent>> LoadAsync(DateTimeOffset? now = null, int pageSize = 100)
        {
            now ??= DateTimeOffset.MinValue;
            var database = CreateClient();

            var mongoCollection = database.GetCollection<MatchFinishedEvent>(_matchfinishedevents);

            var events = await mongoCollection
                .Find(ev => ev.CreatedDate > now)
                .SortBy(s => s.CreatedDate)
                .Limit(pageSize)
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