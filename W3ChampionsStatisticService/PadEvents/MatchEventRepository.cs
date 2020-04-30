using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.PadEvents
{
    public class MatchEventRepository : MongoDbRepositoryBase, IMatchEventRepository
    {
        public MatchEventRepository(MongoClient mongoClient) : base(mongoClient)
        {
        }

        public async Task<List<MatchFinishedEvent>> Load(string lastObjectId = null, int pageSize = 100)
        {
            lastObjectId ??= ObjectId.Empty.ToString();
            var database = CreateClient();

            var mongoCollection = database.GetCollection<MatchFinishedEvent>(nameof(MatchFinishedEvent));

            var events = await mongoCollection.Find(m => m.Id > ObjectId.Parse(lastObjectId))
                .SortBy(s => s.Id)
                .Limit(pageSize)
                .ToListAsync();

            return events;
        }

        public async Task InsertIfNotExisting(MatchFinishedEvent matchFinishedEvent)
        {
            matchFinishedEvent.WasFromSync = true;
            var mongoCollection = CreateCollection<MatchFinishedEvent>();
            var foundEvent = await mongoCollection.Find(e => e.match.id.Equals(matchFinishedEvent.match.id)).FirstOrDefaultAsync();
            if (foundEvent == null)
            {
                await mongoCollection.InsertOneAsync(matchFinishedEvent);
            }
        }

        public async Task<List<RankingChangedEvent>> LoadLatestRanks(int pageSize = 1000)
        {
            var database = CreateClient();

            var mongoCollection = database.GetCollection<RankingChangedEvent>(nameof(RankingChangedEvent));

            var events = await mongoCollection.Find(m => true)
                .SortBy(s => s.id)
                .Limit(pageSize)
                .ToListAsync();

            return events;
        }

        public async Task DeleteRankEvent(ObjectId id)
        {
            var mongoCollection = CreateCollection<RankingChangedEvent>();
            await mongoCollection.DeleteOneAsync(e => e.id == id);
        }
    }
}