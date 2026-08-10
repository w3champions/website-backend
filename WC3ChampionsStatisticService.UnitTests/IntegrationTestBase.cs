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
    /// <summary>
    /// The shared CI instance. It resets itself between runs, which is the only reason
    /// it is safe to point a suite that drops the database at it.
    /// </summary>
    private const string DefaultConnectionString = "mongodb://157.90.1.251:3512/";

    /// <summary>
    /// Where these tests connect. <see cref="Setup"/> drops the entire database before
    /// EVERY test, so whatever this points at is destroyed repeatedly.
    ///
    /// <para>
    /// To run against a local mongo, set the environment variable rather than editing
    /// this file:
    /// <code>TEST_MONGO_CONNECTION_STRING=mongodb://127.0.0.1:27017/ dotnet test</code>
    /// Editing the line was the previous approach and it is a trap - anything that
    /// restores the working tree mid-session (a rebase, a checkout, a stash pop) puts
    /// the shared instance back silently, and the next run finds it.
    /// </para>
    ///
    /// <para>
    /// Deliberately a different variable from <c>MONGO_CONNECTION_STRING</c>, which the
    /// service itself reads: pointing the service somewhere must not silently redirect
    /// a database-dropping test suite there too.
    /// </para>
    /// </summary>
    protected static string ConnectionString =>
        Environment.GetEnvironmentVariable("TEST_MONGO_CONNECTION_STRING") ?? DefaultConnectionString;

    protected readonly MongoClient MongoClient = new(ConnectionString);

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

    protected async Task InsertMatchCanceledEvents(List<MatchCanceledEvent> newEvents)
    {
        foreach (var ev in newEvents)
        {
            await InsertMatchCanceledEvent(ev);
        }
    }

    protected async Task InsertMatchCanceledEvent(MatchCanceledEvent newEvent)
    {
        var database = MongoClient.GetDatabase("W3Champions-Statistic-Service");
        var mongoDatabase = database;
        var mongoCollection = mongoDatabase.GetCollection<MatchCanceledEvent>(nameof(MatchCanceledEvent));
        await mongoCollection.FindOneAndReplaceAsync(
            (Expression<Func<MatchCanceledEvent, bool>>)(ev => ev.match.id == newEvent.match.id),
            newEvent,
            new FindOneAndReplaceOptions<MatchCanceledEvent> { IsUpsert = true });
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
