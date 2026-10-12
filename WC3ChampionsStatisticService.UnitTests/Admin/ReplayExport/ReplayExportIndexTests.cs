using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Mongo2Go;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using W3ChampionsStatisticService.Admin.ReplayExport;

namespace WC3ChampionsStatisticService.Tests.Admin.ReplayExport;

/// <summary>
/// The background index builds against an isolated mongod (Mongo2Go), not the shared remote Mongo.
/// The database that the service meets in production is not known, so these tests start from other states.
/// </summary>
[TestFixture]
public class ReplayExportIndexTests
{
    private const string Database = "W3Champions-Statistic-Service";
    private MongoDbRunner _runner;
    private MongoClient _client;
    private ListLogger<ReplayExportIndexes> _logger;

    [SetUp]
    public async Task SetUp()
    {
        _runner = MongoDbRunner.Start();
        _client = new MongoClient(_runner.ConnectionString);
        _logger = new ListLogger<ReplayExportIndexes>();
        // The database exists in every real deployment. Mongo reports no disk size for a missing database.
        await Collection("Matchup").InsertOneAsync(new BsonDocument("GameMode", 1));
    }

    [TearDown]
    public void TearDown()
    {
        _runner.Dispose();
    }

    [Test]
    public async Task Ensure_BuildsEveryIndexOnce()
    {
        var indexes = new ReplayExportIndexes(_client, _logger);

        await indexes.EnsureAsync(CancellationToken.None);
        await indexes.EnsureAsync(CancellationToken.None);

        foreach (var spec in ReplayExportIndexes.Specs)
        {
            var found = (await IndexesOf(spec.Collection)).Where(i => ReplayExportIndexes.SameKeys(i["key"].AsBsonDocument, spec.Keys)).ToList();
            Assert.AreEqual(1, found.Count, spec.Name);
            Assert.AreEqual(spec.Name, found[0]["name"].AsString);
        }
        Assert.IsEmpty(_logger.At(LogLevel.Warning).Concat(_logger.At(LogLevel.Error)));
    }

    [Test]
    public async Task Ensure_UsesAnIndexWithTheSameKeysAndAnotherName()
    {
        var spec = ReplayExportIndexes.Specs[0];
        await Collection(spec.Collection).Indexes.CreateOneAsync(
            new CreateIndexModel<BsonDocument>(new BsonDocument { { "GameMode", 1.0 }, { "_id", -1.0 } }, new CreateIndexOptions { Name = "made_by_hand" }));

        await new ReplayExportIndexes(_client, _logger).EnsureAsync(CancellationToken.None);

        var names = (await IndexesOf(spec.Collection)).Select(i => i["name"].AsString).ToList();
        CollectionAssert.Contains(names, "made_by_hand");
        CollectionAssert.DoesNotContain(names, spec.Name);
        Assert.IsTrue(_logger.Messages.Any(m => m.Contains("exists as made_by_hand")));
    }

    [Test]
    public async Task Ensure_LogsAConflictAndBuildsTheOtherIndexes()
    {
        var spec = ReplayExportIndexes.Specs[0];
        // The same name with other keys: Mongo refuses the build.
        await Collection(spec.Collection).Indexes.CreateOneAsync(
            new CreateIndexModel<BsonDocument>(new BsonDocument("Season", 1), new CreateIndexOptions { Name = spec.Name }));

        await new ReplayExportIndexes(_client, _logger).EnsureAsync(CancellationToken.None);

        Assert.AreEqual(1, _logger.At(LogLevel.Warning).Count(m => m.Contains(spec.Name) && m.Contains("conflicts")));
        foreach (var other in ReplayExportIndexes.Specs.Skip(1))
        {
            CollectionAssert.Contains((await IndexesOf(other.Collection)).Select(i => i["name"].AsString), other.Name);
        }
    }

    [TestCase(1L)]
    [TestCase(null)]
    public async Task Ensure_BuildsNothingWithoutEnoughFreeDisk(long? freeBytes)
    {
        await new FixedDisk(_client, _logger, freeBytes).EnsureAsync(CancellationToken.None);

        foreach (var spec in ReplayExportIndexes.Specs)
        {
            CollectionAssert.DoesNotContain((await IndexesOf(spec.Collection)).Select(i => i["name"].AsString), spec.Name);
        }
        Assert.AreEqual(ReplayExportIndexes.Specs.Count, _logger.At(LogLevel.Warning).Count(m => m.Contains("not built: free disk")));
        Assert.IsTrue(_logger.At(LogLevel.Warning).All(m => m.Contains("createIndex(")), "The warning tells how to build the index by hand");
    }

    [Test]
    public void RequiredFreeBytes_IsTheReserveAndThreeEntriesPerDocument()
    {
        Assert.AreEqual(ReplayExportIndexes.DiskReserveBytes, ReplayExportIndexes.RequiredFreeBytes(0));
        Assert.AreEqual(ReplayExportIndexes.DiskReserveBytes + 3 * 64 * 10_000_000L, ReplayExportIndexes.RequiredFreeBytes(10_000_000));
    }

    [Test]
    public void SameKeys_ComparesFieldsOrderAndDirection()
    {
        var keys = new BsonDocument { { "GameMode", 1 }, { "_id", -1 } };
        Assert.IsTrue(ReplayExportIndexes.SameKeys(keys, new BsonDocument { { "GameMode", 1.0 }, { "_id", -1L } }));
        Assert.IsFalse(ReplayExportIndexes.SameKeys(keys, new BsonDocument { { "_id", -1 }, { "GameMode", 1 } }));
        Assert.IsFalse(ReplayExportIndexes.SameKeys(keys, new BsonDocument { { "GameMode", 1 }, { "_id", 1 } }));
        Assert.IsFalse(ReplayExportIndexes.SameKeys(keys, new BsonDocument("GameMode", 1)));
    }

    private IMongoCollection<BsonDocument> Collection(string name) => _client.GetDatabase(Database).GetCollection<BsonDocument>(name);

    private async Task<List<BsonDocument>> IndexesOf(string collection) =>
        await (await Collection(collection).Indexes.ListAsync()).ToListAsync();

    private class FixedDisk(MongoClient client, ILogger<ReplayExportIndexes> logger, long? freeBytes) : ReplayExportIndexes(client, logger)
    {
        protected override Task<long?> FreeDiskBytes(IMongoDatabase database, CancellationToken cancellationToken) => Task.FromResult(freeBytes);
    }
}

/// <summary>Keeps the formatted log messages, for checks on warnings and errors.</summary>
public class ListLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    public IEnumerable<string> Messages => _entries.Select(e => e.Message);

    public IEnumerable<string> At(LogLevel level) => _entries.Where(e => e.Level == level).Select(e => e.Message);

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, System.Exception exception, System.Func<TState, System.Exception, string> formatter)
    {
        lock (_entries) _entries.Add((logLevel, formatter(state, exception)));
    }
}
