using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using NUnit.Framework;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Cache;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.Services;

namespace WC3ChampionsStatisticService.Tests;

public class IntegrationTestBase
{
    protected readonly MongoClient MongoClient = new MongoClient("mongodb://w3champions:62473fd9-4e7b-47f3-978f-0a112e926596@localhost:3510?directConnection=true&serverSelectionTimeoutMS=2000&authSource=admin&replicaSet=rs0");
    //protected readonly MongoClient MongoClient = new("mongodb://157.90.1.251:3512/");

    protected PersonalSettingsProvider personalSettingsProvider;

    [SetUp]
    public async Task Setup()
    {
        await MongoClient.DropDatabaseAsync("W3Champions-Statistic-Service");
        personalSettingsProvider = new PersonalSettingsProvider(MongoClient, CreateTestCache<List<PersonalSetting>>());
    }

    protected ICachedDataProvider<T> CreateTestCache<T>() where T : class
    {
        return new InMemoryCachedDataProvider<T>(
            new OptionsWrapper<CacheOptionsFor<T>>(new CacheOptionsFor<T>()),
            new MemoryCache(new MemoryCacheOptions()));
    }

    protected async Task InsertMatchEvents(List<MatchFinishedEvent> newEvents)
    {
        foreach (var ev in newEvents)
        {
            await InsertMatchEvent(ev);
        }
    }

    protected async Task InsertMatchEvent(MatchFinishedEvent newEvent)
    {
        var database = MongoClient.GetDatabase("W3Champions-Statistic-Service");
        var mongoDatabase = database;
        var mongoCollection = mongoDatabase.GetCollection<MatchFinishedEvent>(nameof(MatchFinishedEvent));
        await mongoCollection.FindOneAndReplaceAsync(
            (Expression<Func<MatchFinishedEvent, bool>>)(ev => ev.match.id == newEvent.match.id),
            newEvent,
            new FindOneAndReplaceOptions<MatchFinishedEvent> { IsUpsert = true });
    }

    protected async Task InsertMatchStartedEvent(MatchStartedEvent newEvent)
    {
        var database = MongoClient.GetDatabase("W3Champions-Statistic-Service");
        var mongoDatabase = database;
        var mongoCollection = mongoDatabase.GetCollection<MatchStartedEvent>(nameof(MatchStartedEvent));
        await mongoCollection.FindOneAndReplaceAsync(
            (Expression<Func<MatchStartedEvent, bool>>)(ev => ev.match.id == newEvent.match.id),
            newEvent,
            new FindOneAndReplaceOptions<MatchStartedEvent> { IsUpsert = true });
    }

    protected async Task InsertRankChangedEvent(RankingChangedEvent newEvent)
    {
        var database = MongoClient.GetDatabase("W3Champions-Statistic-Service");
        var mongoDatabase = database;
        var mongoCollection = mongoDatabase.GetCollection<RankingChangedEvent>(nameof(RankingChangedEvent));
        await mongoCollection.FindOneAndReplaceAsync(
            (Expression<Func<RankingChangedEvent, bool>>)(ev => ev.id == newEvent.id),
            newEvent,
            new FindOneAndReplaceOptions<RankingChangedEvent> { IsUpsert = true });
    }
}
