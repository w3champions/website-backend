﻿using System.Collections.Generic;
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

        public async Task Insert(List<MatchFinishedEvent> matchFinishedEvent)
        {
            var mongoCollection = CreateCollection<MatchFinishedEvent>();
            await mongoCollection.InsertManyAsync(matchFinishedEvent);
        }

        public async Task<List<RankingChangedEvent>> CheckoutForRead()
        {
            var mongoCollection = CreateCollection<RankingChangedEvent>();
            var ids = await mongoCollection
                .Find(p => !p.wasSyncedJustNow)
                .Project(p => p.id)
                .ToListAsync();
            var filterDefinition = Builders<RankingChangedEvent>.Filter.In(e => e.id, ids);
            var updateDefinition = Builders<RankingChangedEvent>.Update.Set(e => e.wasSyncedJustNow, true);
            await mongoCollection.UpdateManyAsync(filterDefinition, updateDefinition);
            var ranks = await LoadAll<RankingChangedEvent>(r => ids.Contains(r.id));
            return ranks;
        }

        public Task<List<LeagueConstellationChangedEvent>> LoadLeagueConstellationChanged()
        {
            return LoadAll<LeagueConstellationChangedEvent>();
        }
    }
}