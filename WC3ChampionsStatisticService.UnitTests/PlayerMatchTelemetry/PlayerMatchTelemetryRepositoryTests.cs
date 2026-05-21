using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using W3ChampionsStatisticService.LagReports;
using W3ChampionsStatisticService.PlayerMatchTelemetry;
using PlayerMatchTelemetryDoc = W3ChampionsStatisticService.PlayerMatchTelemetry.PlayerMatchTelemetry;

namespace WC3ChampionsStatisticService.Tests.PlayerMatchTelemetry;

[TestFixture]
public class PlayerMatchTelemetryRepositoryTests : IntegrationTestBase
{
    // MongoDbRepositoryBase hardcodes this database name.
    private const string DatabaseName = "W3Champions-Statistic-Service";

    private PlayerMatchTelemetryRepository _repo;

    [SetUp]
    public void SetUpRepo()
    {
        _repo = new PlayerMatchTelemetryRepository(MongoClient);
    }

    private static PlayerMatchTelemetryEntry MakeEntry(string battleTag) => new()
    {
        BattleTag = battleTag,
        ConnectionType = Transport.QUIC,
        GameLengthMs = 600_000,
        CrashedAt = null,
        DisconnectEvents = new List<DisconnectEvent>(),
        ActionLatencyAggregate = new ActionLatencyAggregate { SampleCount = 100, P50Ms = 42 },
        BucketCount = 3,
        GameTimeOffsetsMs = new BsonBinaryData(new byte[12]),
        MeansMs = new BsonBinaryData(new byte[6]),
        SampleCounts = new BsonBinaryData(new byte[3]),
        SubmittedAt = DateTime.UtcNow,
    };

    [Test]
    public async Task Upsert_then_get_round_trips()
    {
        var gameId = 7777L;
        await _repo.UpsertPlayerEntryAsync(gameId, DateTime.UtcNow,
            MakeEntry("Alice#1234"), TimeSpan.FromDays(90));

        var doc = await _repo.GetByGameIdAsync(gameId);

        Assert.That(doc, Is.Not.Null);
        Assert.That(doc!.Players.Count, Is.EqualTo(1));
        Assert.That(doc.Players[0].BattleTag, Is.EqualTo("Alice#1234"));
    }

    [Test]
    public async Task Two_players_same_game_id_produce_one_doc_with_two_entries()
    {
        var gameId = 7778L;
        await _repo.UpsertPlayerEntryAsync(gameId, DateTime.UtcNow,
            MakeEntry("Alice#1234"), TimeSpan.FromDays(90));
        await _repo.UpsertPlayerEntryAsync(gameId, DateTime.UtcNow,
            MakeEntry("Bob#5678"), TimeSpan.FromDays(90));

        var doc = await _repo.GetByGameIdAsync(gameId);

        Assert.That(doc, Is.Not.Null);
        Assert.That(doc!.Players.Count, Is.EqualTo(2));
        Assert.That(doc.Players.Select(p => p.BattleTag), Is.EquivalentTo(new[] { "Alice#1234", "Bob#5678" }));
    }

    [Test]
    public async Task Resubmit_same_battletag_replaces_entry()
    {
        var gameId = 7779L;

        var entry1 = MakeEntry("Alice#1234");
        entry1.ActionLatencyAggregate.P50Ms = 42;
        await _repo.UpsertPlayerEntryAsync(gameId, DateTime.UtcNow, entry1, TimeSpan.FromDays(90));

        var entry2 = MakeEntry("Alice#1234");
        entry2.ActionLatencyAggregate.P50Ms = 99;
        await _repo.UpsertPlayerEntryAsync(gameId, DateTime.UtcNow, entry2, TimeSpan.FromDays(90));

        var doc = await _repo.GetByGameIdAsync(gameId);

        Assert.That(doc, Is.Not.Null);
        Assert.That(doc!.Players.Count, Is.EqualTo(1));
        Assert.That(doc.Players[0].ActionLatencyAggregate.P50Ms, Is.EqualTo(99));
    }

    [Test]
    public async Task EnsureIndexes_creates_ttl_lookup_and_recency()
    {
        await _repo.EnsureIndexesAsync();

        // Note: `nameof(PlayerMatchTelemetryDoc)` would return the alias identifier,
        // not the underlying type name — use `typeof(...).Name` to get the actual
        // collection name MongoDbRepositoryBase uses.
        var coll = MongoClient
            .GetDatabase(DatabaseName)
            .GetCollection<PlayerMatchTelemetryDoc>(typeof(PlayerMatchTelemetryDoc).Name);
        var indexes = await (await coll.Indexes.ListAsync()).ToListAsync();
        var indexDump = $"count={indexes.Count}; " + string.Join(" | ", indexes.Select(i => i.ToJson()));

        Assert.That(
            indexes.Any(i => i.Contains("expireAfterSeconds")),
            Is.True,
            "TTL index missing — got: " + indexDump);

        Assert.That(
            indexes.Any(i =>
            {
                var name = i.GetValue("name", BsonString.Empty).AsString;
                return name.Contains("BattleTag");
            }),
            Is.True,
            "Players.BattleTag lookup index missing — got: " + indexDump);

        Assert.That(
            indexes.Any(i => i.GetValue("name", BsonString.Empty).AsString == "CreatedAt_recency"),
            Is.True,
            "CreatedAt recency index missing — got: " + indexDump);
    }

    [Test]
    public async Task EnsureIndexes_is_idempotent()
    {
        await _repo.EnsureIndexesAsync();
        // Identical key + options must be a no-op on the second call.
        Assert.DoesNotThrowAsync(async () => await _repo.EnsureIndexesAsync());
    }
}
