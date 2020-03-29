using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Bson;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.MatchEvents
{
    public class MatchEventRepository : MongoDbRepositoryBase, IMatchEventRepository
    {
        public async Task<string> Insert(IList<MatchFinishedEvent> events)
        {
            if (!events.Any()) return ObjectId.Empty.ToString();
            var database = CreateClient();

            var mongoCollection = database.GetCollection<MatchFinishedEvent>(nameof(MatchFinishedEvent));
            await mongoCollection.InsertManyAsync(events);
            return events.Last().Id.ToString();
        }

        public async Task<List<MatchFinishedEvent>> Load(string lastObjectId = null, int pageSize = 100)
        {
            return await Load<MatchFinishedEvent>(lastObjectId, pageSize);
        }

        public MatchEventRepository(DbConnctionInfo connctionInfo) : base(connctionInfo)
        {
        }
    }
}