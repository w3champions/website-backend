using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3C.Domain.MatchmakingService;

namespace W3C.Domain.Repositories;

public class MatchEventRepository : MongoDbRepositoryBase, IMatchEventRepository
{
    public MatchEventRepository(MongoClient mongoClient) : base(mongoClient)
    {
    }

    public async Task<List<MatchFinishedEvent>> Load(string lastObjectId = null, int pageSize = 100)
    {
        var lastObject = string.IsNullOrEmpty(lastObjectId) ? ObjectId.Empty : new ObjectId(lastObjectId);

        return await LoadAll(
            Builders<MatchFinishedEvent>.Filter.Gt(m => m.Id, lastObject), 
            sortBy: Builders<MatchFinishedEvent>.Sort.Ascending(m => m.Id), 
            limit: pageSize);
    }

    public Task<List<MatchStartedEvent>> LoadStartedMatches()
    {
        var delay = ObjectId.GenerateNewId(DateTime.Now.AddSeconds(-20));
        return LoadAll(Builders<MatchStartedEvent>.Filter.Lt(m => m.Id, delay), limit: 1000);
    }

    public Task<List<MatchCanceledEvent>> LoadCanceledMatches()
    {
        var now = ObjectId.GenerateNewId(DateTime.Now);
        return LoadAll(Builders<MatchCanceledEvent>.Filter.Lt(m => m.Id, now), limit: 1000);
    }

    public async Task<bool> InsertIfNotExisting(MatchFinishedEvent matchFinishedEvent, int attempt = 0)
    {
        matchFinishedEvent.WasFromSync = true;

        var filter = Builders<MatchFinishedEvent>.Filter.Eq(e => e.match.id, matchFinishedEvent.match.id);
        var update = Builders<MatchFinishedEvent>.Update
            .SetOnInsert(e => e, matchFinishedEvent); // Only set on insert

        var result = await UpdateOneAsync(
            filter,
            update,
            new UpdateOptions { IsUpsert = true }
        );

        if (result.UpsertedId != null)
        {
            Console.WriteLine($"({attempt}) INSERTED: {matchFinishedEvent.match.id}");
            return true;
        }

        Console.WriteLine($"({attempt}) ALREADY EXISTS: {matchFinishedEvent.match.id}");
        return false;
    }

    public Task<List<RankingChangedEvent>> CheckoutForRead()
    {
        return Checkout<RankingChangedEvent>();
    }

    private async Task<List<T>> Checkout<T>() where T : ISyncable
    {
        var filter = Builders<T>.Filter.Eq(p => p.wasSyncedJustNow, false);
        var items = await LoadAll(filter, limit: 1000);

        if (items.Count == 0)
            return items;

        var ids = items.Select(p => p.id).ToList();
        var updateFilter = Builders<T>.Filter.In(p => p.id, ids);
        var update = Builders<T>.Update.Set(p => p.wasSyncedJustNow, true);

        await UpdateManyAsync(updateFilter, update);

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

    public Task DeleteCanceledEvent(ObjectId nextEventId)
    {
        return Delete<MatchCanceledEvent>(e => e.Id == nextEventId);
    }
}
