using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using W3ChampionsStatisticService.MatchEvents;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.MongoDb
{
    public class MatchEventRepository : MongoDbRepositoryBase, IMatchEventRepository
    {
        private readonly string _matchfinishedevents = "MatchFinishedEvents";


        public async Task<string> Insert(IList<MatchFinishedEvent> events)
        {
            if (!events.Any()) return ObjectId.Empty.ToString();
            var database = CreateClient();

            var mongoCollection = database.GetCollection<MatchFinishedEvent>(_matchfinishedevents);
            await mongoCollection.InsertManyAsync(events);
            return events.Last().Id.ToString();
        }

        public async Task<List<MatchFinishedEvent>> Load(string lastObjectId = null, int pageSize = 100)
        {
            return await Load<MatchFinishedEvent>(_matchfinishedevents, lastObjectId, pageSize);
        }

        public MatchEventRepository(DbConnctionInfo connctionInfo) : base(connctionInfo)
        {
        }
    }
}