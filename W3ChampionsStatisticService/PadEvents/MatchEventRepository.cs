using System;
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

            var mongoCollection = CreateCollection<MatchFinishedEvent>();

            var events = await mongoCollection.Find(m => m.Id > ObjectId.Parse(lastObjectId))
                .SortBy(s => s.Id)
                .Limit(pageSize)
                .ToListAsync();

            return events;
        }

        public async Task<List<MatchStartedEvent>> LoadStartedMatches(string lastObjectId = null, int pageSize = 100)
        {
            lastObjectId ??= ObjectId.Empty.ToString();

            var mongoCollection = CreateCollection<MatchStartedEvent>();
            var version = ObjectId.Parse(lastObjectId);
            var delay = ObjectId.GenerateNewId(DateTime.Now.AddSeconds(-20));

            var events = await mongoCollection.Find(m =>
                m.Id > version && m.Id < delay)
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
                Console.WriteLine($"INSERTED: {matchFinishedEvent.match.id}");
            }
            else
            {
                Console.WriteLine($"EVENT WAS PRESENT ALLREADY: {foundEvent.match.id}");
            }
        }

        public async Task Insert(List<MatchFinishedEvent> matchFinishedEvent)
        {
            var mongoCollection = CreateCollection<MatchFinishedEvent>();
            await mongoCollection.InsertManyAsync(matchFinishedEvent);
        }

        public Task<List<RankingChangedEvent>> CheckoutForRead()
        {
            return Checkout<RankingChangedEvent>();
        }

        private async Task<List<T>> Checkout<T>() where T: ISyncable
        {
            var mongoCollection = CreateCollection<T>();
            var ids = await mongoCollection
                .Find(p => !p.wasSyncedJustNow)
                .Project(p => p.id)
                .ToListAsync();
            var filterDefinition = Builders<T>.Filter.In(e => e.id, ids);
            var updateDefinition = Builders<T>.Update.Set(e => e.wasSyncedJustNow, true);
            await mongoCollection.UpdateManyAsync(filterDefinition, updateDefinition);
            var items = await LoadAll<T>(r => ids.Contains(r.id));
            return items;
        }

        public Task<List<LeagueConstellationChangedEvent>> LoadLeagueConstellationChanged()
        {
            return Checkout<LeagueConstellationChangedEvent>();
        }

        public Task DeleteStartedEvent(ObjectId nextEventId)
        {
            return Delete<MatchStartedEvent>(e => e.Id == nextEventId);
        }
    }
}