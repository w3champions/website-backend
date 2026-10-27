using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mongo2Go;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.W3ChampionsStats;
using W3ChampionsStatisticService.W3ChampionsStats.MapsPerSeasons;
using W3ChampionsStatisticService.W3ChampionsStats.OverallRaceAndWinStats;

namespace WC3ChampionsStatisticService.Tests.Statistics;

// Per-test Mongo2Go runner (mirrors Friend/FriendRepositoryTests.cs) — deliberately NOT
// IntegrationTestBase, which points at a shared remote Mongo and drops it.
[TestFixture]
public class MapKeyPersistenceTests
{
    private const string EchoIslesFile = "1051_w3c_260615_2308_EchoIsles_v2.2.w3x";
    private const string LegacyEchoIslesKey = "1w3c2606152308EchoIslesv2_2";

    private MongoDbRunner _runner;
    private MongoClient _mongoClient;
    private W3StatsRepo _repository;

    [SetUp]
    public void Setup()
    {
        _runner = MongoDbRunner.Start();
        _mongoClient = new MongoClient(_runner.ConnectionString);
        _repository = new W3StatsRepo(_mongoClient);
    }

    [TearDown]
    public void TearDown()
    {
        _runner.Dispose();
    }

    [Test]
    public async Task MapsPerSeason_SaveAndLoad_KeepsIdAndName()
    {
        var stats = MapsPerSeason.Create(25);
        stats.Count(EchoIslesFile, 1051, "Echo Isles v2", GameMode.GM_1v1);

        await _repository.Save(stats);
        var loaded = await _repository.LoadMapsPerSeason(25);

        var entry = loaded.MatchesOnMapPerModes.Single(m => m.GameMode == GameMode.GM_1v1).Maps.Single();
        Assert.That(entry.Map, Is.EqualTo(EchoIslesFile));
        Assert.That(entry.MapId, Is.EqualTo(1051));
        Assert.That(entry.MapName, Is.EqualTo("Echo Isles v2"));
        Assert.That(entry.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task OverallRaceAndWinStat_SaveAndLoad_KeepsMapIdAndNameUnderDottedPatch()
    {
        var stat = new OverallRaceAndWinStat(0);
        stat.Apply("Overall", null, "Overall", Race.HU, Race.UD, true, "1.32.5");
        stat.Apply(EchoIslesFile, 1051, "Echo Isles v2", Race.HU, Race.UD, true, "1.32.5");

        await _repository.Save(stat);
        var loaded = await _repository.LoadRaceVsRaceStat(0);

        var entries = loaded.PatchToStatsPerModes["1.32.5"];
        Assert.That(entries.Select(e => e.Map), Is.EqualTo(new[] { "Overall", EchoIslesFile }));
        Assert.That(entries[1].MapId, Is.EqualTo(1051));
        Assert.That(entries[1].MapName, Is.EqualTo("Echo Isles v2"));
    }

    // Pins why the backfill detects migrated entries by value: saving a document through the new
    // classes writes MapId: null onto legacy entries, so an element-presence check would skip them.
    [Test]
    public async Task MapsPerSeason_LegacyDocument_RoundTripStampsNullMapIdOnLegacyEntries()
    {
        var legacy = MapsPerSeason.Create(-1);
        legacy.Count(LegacyEchoIslesKey, null, null, GameMode.GM_1v1);
        var legacyDocument = legacy.ToBsonDocument();
        foreach (var map in legacyDocument["MatchesOnMapPerModes"].AsBsonArray
                     .SelectMany(m => m["Maps"].AsBsonArray)
                     .Select(m => m.AsBsonDocument))
        {
            map.Remove("MapId");
            map.Remove("MapName");
        }
        var collection = Collection(nameof(MapsPerSeason));
        await collection.InsertOneAsync(legacyDocument);

        var stats = await _repository.LoadMapsPerSeason(-1);
        stats.Count(EchoIslesFile, 1051, "Echo Isles v2", GameMode.GM_1v1);
        await _repository.Save(stats);

        var raw = await collection.Find(FilterDefinition<BsonDocument>.Empty).SingleAsync();
        // Two buckets (Undefined + 1v1), each with the legacy and the new entry.
        var maps = raw["MatchesOnMapPerModes"].AsBsonArray
            .SelectMany(m => m["Maps"].AsBsonArray)
            .Select(m => m.AsBsonDocument)
            .ToList();
        var legacyEntries = maps.Where(m => m["Map"] == LegacyEchoIslesKey).ToList();
        var newEntries = maps.Where(m => m["Map"] == EchoIslesFile).ToList();
        Assert.That(legacyEntries.Count, Is.EqualTo(2));
        Assert.That(legacyEntries.All(m => m.Contains("MapId") && m["MapId"].IsBsonNull), Is.True);
        Assert.That(legacyEntries.All(m => m["Count"].AsInt32 == 1), Is.True);
        Assert.That(newEntries.Count, Is.EqualTo(2));
        Assert.That(newEntries.All(m => m["MapId"].AsInt32 == 1051), Is.True);
    }

    [Test]
    public async Task OverallRaceAndWinStat_LegacyDocument_KeepsOverallFirstAndStampsNullMap()
    {
        var legacy = new OverallRaceAndWinStat(0)
        {
            PatchToStatsPerModes = new Dictionary<string, List<MapToRaceVsRaceRatio>>
            {
                ["All"] =
                [
                    new MapToRaceVsRaceRatio { MapName = "Overall" },
                    new MapToRaceVsRaceRatio { MapName = LegacyEchoIslesKey },
                ]
            }
        };
        var legacyDocument = legacy.ToBsonDocument();
        foreach (var entry in legacyDocument["PatchToStatsPerModes"]["All"].AsBsonArray.Select(e => e.AsBsonDocument))
        {
            entry.Remove("Map");
            entry.Remove("MapId");
        }
        var collection = Collection(nameof(OverallRaceAndWinStat));
        await collection.InsertOneAsync(legacyDocument);

        var stat = await _repository.LoadRaceVsRaceStat(0);
        stat.Apply("Overall", null, "Overall", Race.HU, Race.UD, true, "All");
        stat.Apply(EchoIslesFile, 1051, "Echo Isles v2", Race.HU, Race.UD, true, "All");
        await _repository.Save(stat);

        var raw = await collection.Find(FilterDefinition<BsonDocument>.Empty).SingleAsync();
        var entries = raw["PatchToStatsPerModes"]["All"].AsBsonArray.Select(e => e.AsBsonDocument).ToList();
        Assert.That(entries.Count, Is.EqualTo(3));
        Assert.That(entries[0]["Map"].AsString, Is.EqualTo("Overall"));
        Assert.That(entries[0]["MapName"].AsString, Is.EqualTo("Overall"));
        Assert.That(entries[1]["Map"].IsBsonNull, Is.True);
        Assert.That(entries[1]["MapId"].IsBsonNull, Is.True);
        Assert.That(entries[1]["MapName"].AsString, Is.EqualTo(LegacyEchoIslesKey));
        Assert.That(entries[2]["Map"].AsString, Is.EqualTo(EchoIslesFile));
        Assert.That(entries[2]["MapId"].AsInt32, Is.EqualTo(1051));
    }

    private IMongoCollection<BsonDocument> Collection(string name)
    {
        return _mongoClient.GetDatabase("W3Champions-Statistic-Service").GetCollection<BsonDocument>(name);
    }
}
