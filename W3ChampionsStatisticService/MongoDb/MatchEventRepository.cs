using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using W3ChampionsStatisticService.MatchEvents;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.MongoDb
{
    public class MatchFinishedEventDto
    {
        public MatchFinishedEvent MatchFinishedEvent { get; set; }

        [BsonId]
        public ObjectId Id { get; set; }

        public MatchFinishedEventDto(MatchFinishedEvent matchFinishedEvent)
        {
            MatchFinishedEvent = matchFinishedEvent;
        }
    }
    public class MatchEventRepository : IMatchEventRepository
    {
        private readonly DbConnctionInfo _connectionInfo;
        private string databaseName = "W3Champions-Statistic-Service";
        private string _matchfinishedevents = "MatchFinishedEvents";

        public MatchEventRepository(DbConnctionInfo connectionInfo)
        {
            _connectionInfo = connectionInfo;
        }

        public async Task<string> Insert(IList<MatchFinishedEvent> events)
        {
            if (!events.Any()) return ObjectId.Empty.ToString();
            var database = CreateClient();

            var mongoCollection = database.GetCollection<MatchFinishedEventDto>(_matchfinishedevents);
            var matchFinishedEventDtos = events.Select(e => new MatchFinishedEventDto(e)).ToList();
            await mongoCollection.InsertManyAsync(matchFinishedEventDtos);
            return matchFinishedEventDtos.Last().Id.ToString();
        }

        public async Task<IList<MatchFinishedEvent>> Load(string lastObjectId = null, int pageSize = 100)
        {
            lastObjectId ??= ObjectId.Empty.ToString();
            var database = CreateClient();

            var mongoCollection = database.GetCollection<MatchFinishedEventDto>(_matchfinishedevents);
            var filterBuilder = Builders<MatchFinishedEventDto>.Filter;
            var filter = filterBuilder.Gt(x => x.Id, ObjectId.Parse(lastObjectId));

            var events = await mongoCollection.Find(filter)
                .SortBy(s => s.Id)
                .Limit(pageSize)
                .ToListAsync();

            return events.Select(e => e.MatchFinishedEvent).ToList();
        }

        private IMongoDatabase CreateClient()
        {
            var client = new MongoClient(_connectionInfo.ConnectionString);
            var database = client.GetDatabase(databaseName);
            return database;
        }
    }
}