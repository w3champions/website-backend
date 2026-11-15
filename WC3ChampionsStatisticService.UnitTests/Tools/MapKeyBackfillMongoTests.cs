using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Mongo2Go;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using W3ChampionsStatisticService.Tools.MapKeyBackfill;

namespace WC3ChampionsStatisticService.Tests.Tools;

// Per-test Mongo2Go runner, deliberately not IntegrationTestBase.
[TestFixture]
public class MapKeyBackfillMongoTests
{
    private const string Database = "MapKeyBackfillTests";
    private const string EchoFile = "1051_w3c_260615_2308_EchoIsles_v2.2.w3x";
    private const string EchoKey = "1w3c2606152308EchoIslesv2_2";

    private MongoDbRunner _runner;
    private IMongoDatabase _database;
    private string _directory;
    private List<string> _log;

    [SetUp]
    public async Task Setup()
    {
        _runner = MongoDbRunner.Start();
        _database = new MongoClient(_runner.ConnectionString).GetDatabase(Database);
        _directory = Path.Combine(Path.GetTempPath(), "map-key-backfill-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _log = new List<string>();
        File.WriteAllText(Path.Combine(_directory, "manual-map.json"), MapKeyResolverTests.ManualJson);

        var echoEvent = ObjectId.GenerateNewId(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));
        var otherEchoEvent = ObjectId.GenerateNewId(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
        var season5Match = ObjectId.GenerateNewId(new DateTime(2021, 3, 1, 0, 0, 0, DateTimeKind.Utc));

        await Collection("MatchFinishedEvent").InsertManyAsync(new[]
        {
            Event(echoEvent, "m1"),
            Event(otherEchoEvent, "m2"),
            new BsonDocument { ["_id"] = ObjectId.GenerateNewId(new DateTime(2019, 9, 1, 0, 0, 0, DateTimeKind.Utc)), ["match"] = new BsonDocument { ["map"] = BsonNull.Value, ["mapId"] = 0, ["gameMode"] = 1 } }
        });

        await Collection("Matchup").InsertManyAsync(new[]
        {
            Matchup(echoEvent, "m1", EchoKey, 25),
            // Its _id doesn't match the event: found through MatchId.
            Matchup(ObjectId.GenerateNewId(new DateTime(2026, 8, 2, 0, 0, 0, DateTimeKind.Utc)), "m2", EchoKey, 25),
            Matchup(season5Match, "m3", "lastrefuge", 5, mapId: BsonNull.Value, mapName: BsonNull.Value)
        });

        await Collection("MapsPerSeason").InsertManyAsync(new[]
        {
            MapsPerSeason("-1", -1, ("lastrefuge", 4), (EchoKey, 2)),
            MapsPerSeason("5", 5, ("lastrefuge", 4)),
            MapsPerSeason("25", 25, (EchoKey, 2))
        });

        await Collection("PlayerHeroStats").InsertOneAsync(BsonDocument.Parse($$"""
            { "_id": "25_player#1", "Season": 25, "HeroStatsItemList": [ { "HeroId": "archmage", "Stats": [ { "Race": 1, "WinLossesOnMap": [
              { "Map": "Overall", "WinLosses": [ { "Race": 2, "Wins": 1, "Losses": 0 } ] },
              { "Map": "{{EchoKey}}", "WinLosses": [ { "Race": 2, "Wins": 1, "Losses": 0 } ] } ] } ] } ] }
            """));
        await Collection("PlayerRaceOnMapVersusRaceRatio").InsertOneAsync(BsonDocument.Parse($$"""
            { "_id": "25_player#1", "Season": 25, "RaceWinsOnMapByPatch": { "2.0.3": [ { "Race": 16, "WinLossesOnMap": [ { "Map": "{{EchoKey}}", "WinLosses": [] } ] } ] }, "RaceWinsOnMap": [] }
            """));
        await Collection("OverallRaceAndWinStat").InsertOneAsync(BsonDocument.Parse($$"""
            { "_id": 0, "MmrRange": 0, "PatchToStatsPerModes": { "2.0.3": [ { "MapName": "Overall", "Ratio": [] }, { "MapName": "{{EchoKey}}", "Ratio": [] } ] }, "StatsPerModes": [] }
            """));
    }

    [TearDown]
    public void TearDown()
    {
        _runner.Dispose();
        Directory.Delete(_directory, true);
    }

    [Test]
    public async Task DryRun_WritesOnlyTheReport()
    {
        var before = await Snapshot();

        var report = await Run();

        Assert.AreEqual(before, await Snapshot());
        Assert.IsTrue(File.Exists(Path.Combine(_directory, "report.csv")));
        Assert.AreEqual(3, report.TotalsFor("Matchup").DocumentsChanged);
        Assert.AreEqual(0, report.TotalsFor("Matchup").DocumentsWritten);
    }

    [Test]
    public async Task Apply_RewritesEveryCollection_AndASecondApplyChangesNothing()
    {
        var first = await Run("--apply");

        var matchups = await Collection("Matchup").Find(FilterDefinition<BsonDocument>.Empty).Sort(Builders<BsonDocument>.Sort.Ascending("_id")).ToListAsync();
        Assert.AreEqual(EchoFile, matchups[2]["Map"].AsString);
        Assert.AreEqual(EchoFile, matchups[1]["Map"].AsString);
        Assert.AreEqual("lastrefuge", matchups[0]["Map"].AsString);
        Assert.AreEqual(3, matchups[0]["MapId"].AsInt32);
        Assert.AreEqual("Last Refuge", matchups[0]["MapName"].AsString);

        var allSeasons = await Collection("MapsPerSeason").Find(Builders<BsonDocument>.Filter.Eq("_id", "-1")).SingleAsync();
        var oneVsOne = allSeasons["MatchesOnMapPerModes"].AsBsonArray.Single(b => b["GameMode"] == 1)["Maps"].AsBsonArray;
        CollectionAssert.AreEquivalent(new[] { EchoFile, "lastrefuge" }, oneVsOne.Select(m => m["Map"].AsString));
        Assert.AreEqual(3, oneVsOne.Single(m => m["Map"] == "lastrefuge")["MapId"].AsInt32);

        var hero = await Collection("PlayerHeroStats").Find(FilterDefinition<BsonDocument>.Empty).SingleAsync();
        Assert.AreEqual(EchoFile, hero["HeroStatsItemList"][0]["Stats"][0]["WinLossesOnMap"][1]["Map"].AsString);
        var race = await Collection("PlayerRaceOnMapVersusRaceRatio").Find(FilterDefinition<BsonDocument>.Empty).SingleAsync();
        Assert.AreEqual(1051, race["RaceWinsOnMapByPatch"]["2.0.3"][0]["WinLossesOnMap"][0]["MapId"].AsInt32);
        var overall = await Collection("OverallRaceAndWinStat").Find(FilterDefinition<BsonDocument>.Empty).SingleAsync();
        Assert.AreEqual("Overall", overall["PatchToStatsPerModes"]["2.0.3"][0]["Map"].AsString);
        Assert.AreEqual(EchoFile, overall["PatchToStatsPerModes"]["2.0.3"][1]["Map"].AsString);

        Assert.AreEqual(3, first.TotalsFor("Matchup").DocumentsWritten);
        Assert.AreEqual(3, first.TotalsFor("MapsPerSeason").DocumentsWritten);

        var afterFirst = await Snapshot();
        var second = await Run("--apply");

        Assert.AreEqual(afterFirst, await Snapshot());
        foreach (var collection in MapKeyBackfillOptions.SupportedCollections)
        {
            Assert.AreEqual(0, second.TotalsFor(collection).DocumentsChanged, collection);
        }
    }

    [Test]
    public async Task Apply_GivesTheEventToEveryMatchupWithItsMatchId()
    {
        const string otherFile = "w3c_echoisles_duplicate.w3x";
        var evt = Event(ObjectId.GenerateNewId(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)), "m9");
        evt["match"]["map"] = "W3Champions/" + otherFile;
        await Collection("MatchFinishedEvent").InsertOneAsync(evt);
        // Two Matchups of one match, on the same page, neither with the event's _id.
        await Collection("Matchup").InsertManyAsync(new[]
        {
            Matchup(ObjectId.GenerateNewId(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)), "m9", EchoKey, 25),
            Matchup(ObjectId.GenerateNewId(new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc)), "m9", EchoKey, 25)
        });

        await Run("--apply", "--collections", "Matchup");

        var sameMatch = await Collection("Matchup").Find(Builders<BsonDocument>.Filter.Eq("MatchId", "m9")).ToListAsync();
        Assert.AreEqual(2, sameMatch.Count);
        Assert.That(sameMatch.Select(d => d["Map"].AsString), Is.All.EqualTo(otherFile));
    }

    [Test]
    public async Task GuardedReplace_LeavesADocumentThatChangedAfterItWasRead()
    {
        var heroStats = Collection("PlayerHeroStats");
        var byId = Builders<BsonDocument>.Filter.Eq("_id", "25_player#1");
        var read = await heroStats.Find(byId).SingleAsync();

        // A handler saves a game in between.
        await heroStats.UpdateOneAsync(byId, Builders<BsonDocument>.Update.Set("HeroStatsItemList.0.Stats.0.WinLossesOnMap.1.WinLosses.0.Wins", 2));
        var skipped = await heroStats.BulkWriteAsync(new[] { MapKeyBackfillRunner.GuardedReplace(read, Migrated(read)) });

        Assert.AreEqual(0, skipped.MatchedCount);
        var afterHandler = await heroStats.Find(byId).SingleAsync();
        Assert.AreEqual(EchoKey, MapOf(afterHandler).AsString);
        Assert.AreEqual(2, afterHandler["HeroStatsItemList"][0]["Stats"][0]["WinLossesOnMap"][1]["WinLosses"][0]["Wins"].AsInt32);

        // Read again, the replace goes through and keeps the handler's game.
        var applied = await heroStats.BulkWriteAsync(new[] { MapKeyBackfillRunner.GuardedReplace(afterHandler, Migrated(afterHandler)) });

        Assert.AreEqual(1, applied.ModifiedCount);
        var migrated = await heroStats.Find(byId).SingleAsync();
        Assert.AreEqual(EchoFile, MapOf(migrated).AsString);
        Assert.AreEqual(2, migrated["HeroStatsItemList"][0]["Stats"][0]["WinLossesOnMap"][1]["WinLosses"][0]["Wins"].AsInt32);

        static BsonValue MapOf(BsonDocument doc) => doc["HeroStatsItemList"][0]["Stats"][0]["WinLossesOnMap"][1]["Map"];

        static BsonDocument Migrated(BsonDocument doc)
        {
            var copy = doc.DeepClone().AsBsonDocument;
            copy["HeroStatsItemList"][0]["Stats"][0]["WinLossesOnMap"][1]["Map"] = EchoFile;
            return copy;
        }
    }

    [Test]
    public async Task ServerHostName_OfALocalServer_IsThisMachine()
    {
        var hostName = await MapKeyBackfillSafetyGuard.ServerHostNameAsync(_runner.ConnectionString);

        Assert.IsTrue(MapKeyBackfillSafetyGuard.IsThisMachine(hostName, Environment.MachineName), hostName);
    }

    [Test]
    public async Task EventCatalog_WrittenOnce_GivesTheSameReportWhenReadBack()
    {
        var catalogPath = Path.Combine(_directory, "events.jsonl");
        await Run("--write-event-catalog", catalogPath);
        var aggregated = File.ReadAllText(Path.Combine(_directory, "report.csv"));

        var catalog = EventMapCatalog.Load(catalogPath);
        Assert.AreEqual(Database, catalog.Header.Database);
        Assert.AreEqual(2, catalog.Header.Events);
        Assert.AreEqual(1, catalog.Groups.Count);
        Assert.AreEqual(EchoFile, catalog.Groups[0].File);

        await Collection("MatchFinishedEvent").DeleteManyAsync(FilterDefinition<BsonDocument>.Empty);
        await Run("--event-catalog", catalogPath, "--collections", "MapsPerSeason,PlayerHeroStats,PlayerRaceOnMapVersusRaceRatio,OverallRaceAndWinStat");
        var fromFile = File.ReadAllText(Path.Combine(_directory, "report.csv"));

        var withoutMatchup = string.Join("\n", aggregated.Split('\n').Where(l => !l.StartsWith("Matchup,")));
        Assert.AreEqual(withoutMatchup, fromFile);
    }

    private async Task<MapKeyReport> Run(params string[] extra)
    {
        var args = new[]
        {
            "backfill-map-keys",
            "--connection-string", _runner.ConnectionString,
            "--database", Database,
            "--manual-map", Path.Combine(_directory, "manual-map.json"),
            "--report", Path.Combine(_directory, "report.csv"),
            "--aggregation-pause-ms", "0",
            "--batch-size", "2"
        }.Concat(extra).ToArray();
        var options = MapKeyBackfillOptions.Parse(args);
        var target = MapKeyBackfillSafetyGuard.DescribeTarget(options.ConnectionString);
        MapKeyBackfillSafetyGuard.EnsureApplyAllowed(options, target);

        var runner = new MapKeyBackfillRunner(options, target, _log.Add);
        await runner.RunAsync();
        return runner.Report;
    }

    private async Task<string> Snapshot()
    {
        var parts = new List<string>();
        foreach (var name in MapKeyBackfillOptions.SupportedCollections)
        {
            var docs = await Collection(name).Find(FilterDefinition<BsonDocument>.Empty).Sort(Builders<BsonDocument>.Sort.Ascending("_id")).ToListAsync();
            parts.Add(name + ":" + string.Join(",", docs.Select(d => d.ToJson())));
        }

        return string.Join("\n", parts);
    }

    private IMongoCollection<BsonDocument> Collection(string name) => _database.GetCollection<BsonDocument>(name);

    private static BsonDocument Event(ObjectId id, string matchId)
    {
        return new BsonDocument
        {
            ["_id"] = id,
            ["match"] = new BsonDocument
            {
                ["_id"] = matchId,
                ["map"] = "W3Champions/" + EchoFile,
                ["mapId"] = 1051,
                ["mapName"] = "Echo Isles v2",
                ["season"] = 25,
                ["gameMode"] = 1
            }
        };
    }

    private static BsonDocument Matchup(ObjectId id, string matchId, string map, int season, BsonValue mapId = null, BsonValue mapName = null)
    {
        return new BsonDocument
        {
            ["_id"] = id,
            ["Map"] = map,
            ["MapName"] = mapName ?? "Echo Isles v2",
            ["MapId"] = mapId ?? 1051,
            ["MatchId"] = matchId,
            ["GameMode"] = 1,
            ["Season"] = season
        };
    }

    private static BsonDocument MapsPerSeason(string id, int season, params (string Map, int Count)[] maps)
    {
        BsonArray Entries() => new(maps.Select(m => new BsonDocument { ["Map"] = m.Map, ["Count"] = m.Count }));
        return new BsonDocument
        {
            ["_id"] = id,
            ["MatchesOnMapPerModes"] = new BsonArray
            {
                new BsonDocument { ["GameMode"] = 0, ["Maps"] = Entries() },
                new BsonDocument { ["GameMode"] = 1, ["Maps"] = Entries() }
            },
            ["Season"] = season
        };
    }
}
