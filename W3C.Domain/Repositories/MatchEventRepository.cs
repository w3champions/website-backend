using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using W3C.Domain.MatchmakingService;

namespace W3C.Domain.Repositories;

public class MatchEventRepository(MongoClient mongoClient) : MongoDbRepositoryBase(mongoClient), IMatchEventRepository
{
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

    public Task<List<MatchStartedEvent>> LoadStartedMatches()
    {
        var delay = ObjectId.GenerateNewId(DateTime.Now.AddSeconds(-20));
        return LoadAll<MatchStartedEvent>(m => m.Id < delay, 1000);
    }

    public async Task<bool> InsertIfNotExisting(MatchFinishedEvent matchFinishedEvent, int i = 0)
    {
        matchFinishedEvent.WasFromSync = true;
        var mongoCollection = CreateCollection<MatchFinishedEvent>();
        var foundEvent = await mongoCollection.Find(e => e.match.id.Equals(matchFinishedEvent.match.id)).FirstOrDefaultAsync();
        if (foundEvent == null)
        {
            await mongoCollection.InsertOneAsync(matchFinishedEvent);
            Console.WriteLine($"({i}) INSERTED: {matchFinishedEvent.match.id}");
            return true;
        }

        Console.WriteLine($"({i}) EVENT WAS PRESENT ALLREADY: {foundEvent.match.id}");
        return false;
    }

    public Task<List<RankingChangedEvent>> CheckoutForRead()
    {
        return Checkout<RankingChangedEvent>();
    }

    private async Task<List<T>> Checkout<T>() where T : ISyncable
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
