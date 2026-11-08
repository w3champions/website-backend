using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mongo2Go;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.Maps;
using W3ChampionsStatisticService.Matches;

namespace WC3ChampionsStatisticService.Tests.Matchups;

// Ongoing-matches filter by map name (two files of the same map share one MapName). With current
// names, a current-name mode compares the name matchmaking has now.
// Per-test Mongo2Go runner (mirrors Friend/FriendRepositoryTests.cs) — deliberately NOT
// IntegrationTestBase, which points at a shared remote Mongo and drops it.
[TestFixture]
public class OngoingMatchesCacheTests
{
    private const string EchoIslesFile = "1051_w3c_260615_2308_EchoIsles_v2.2.w3x";
    private const string EchoIslesOtherFile = "1051_w3c_260919_1153_EchoIsles_v2.2.w3x";
    private const string ShallowGraveFile = "w3c_250622_1057_ShallowGrave_v1.5.w3x";
    // Played while id 1051 was still named "Echo Isles 2.0".
    private const string EchoIslesOldFile = "w3c_EchoIsles_v2.2.w3x";
    private const string LegionTdFile = "Legion_TD_11.0_W3C_TeamOZE.w3x";

    private static readonly CurrentMapNames CurrentNames = new(new Dictionary<int, string>
    {
        [1051] = "Echo Isles v2",
        [5072] = "Legion TD: X3",
    });

    private MongoDbRunner _runner;
    private OngoingMatchesCache _cache;

    [SetUp]
    public void Setup()
    {
        _runner = MongoDbRunner.Start();
        _cache = new OngoingMatchesCache(new MongoClient(_runner.ConnectionString), TestDtoHelper.CreateMockedTracingService().Object);
        _cache.Upsert(Ongoing("m1", EchoIslesFile, 1051, "Echo Isles v2"));
        _cache.Upsert(Ongoing("m2", EchoIslesOtherFile, 1051, "Echo Isles v2"));
        _cache.Upsert(Ongoing("m3", ShallowGraveFile, 61, "Shallow Grave"));
    }

    [TearDown]
    public void TearDown()
    {
        _runner.Dispose();
    }

    [Test]
    public async Task MapName_ReturnsEveryFileOfThatMap()
    {
        var matches = await _cache.LoadOnGoingMatches(GameMode.GM_1v1, GateWay.Undefined, 0, 100, "Overall", 0, null, "startTimeDescending", "Echo Isles v2");
        var count = await _cache.CountOnGoingMatches(GameMode.GM_1v1, GateWay.Undefined, "Overall", 0, null, "Echo Isles v2");

        Assert.That(matches.Select(m => m.MatchId), Is.EquivalentTo(new[] { "m1", "m2" }));
        Assert.That(count, Is.EqualTo(2));
    }

    [Test]
    [TestCase("Overall")]
    [TestCase(null)]
    [TestCase("")]
    public async Task MapName_OverallNullOrEmpty_DoesNotFilter(string mapName)
    {
        var matches = await _cache.LoadOnGoingMatches(GameMode.GM_1v1, GateWay.Undefined, 0, 100, "Overall", 0, null, "startTimeDescending", mapName);
        var count = await _cache.CountOnGoingMatches(GameMode.GM_1v1, GateWay.Undefined, "Overall", 0, null, mapName);

        Assert.That(matches.Count, Is.EqualTo(3));
        Assert.That(count, Is.EqualTo(3));
    }

    [Test]
    public async Task Map_StillFiltersByFile()
    {
        var matches = await _cache.LoadOnGoingMatches(GameMode.GM_1v1, GateWay.Undefined, 0, 100, EchoIslesOtherFile, 0, null, "startTimeDescending", "Overall");
        var count = await _cache.CountOnGoingMatches(GameMode.GM_1v1, GateWay.Undefined, EchoIslesOtherFile, 0, null, "Overall");

        Assert.That(matches.Select(m => m.MatchId), Is.EqualTo(new[] { "m2" }));
        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public async Task MapAndMapName_AreCombined()
    {
        var count = await _cache.CountOnGoingMatches(GameMode.GM_1v1, GateWay.Undefined, ShallowGraveFile, 0, null, "Echo Isles v2");

        Assert.That(count, Is.EqualTo(0));
    }

    [Test]
    public async Task MapName_WithCurrentNames_FindsAMatchStoredUnderTheOldName()
    {
        _cache.Upsert(Ongoing("m4", EchoIslesOldFile, 1051, "Echo Isles 2.0"));

        var matches = await _cache.LoadOnGoingMatches(GameMode.GM_1v1, GateWay.Undefined, 0, 100, "Overall", 0, null, "startTimeDescending", "Echo Isles v2", CurrentNames);
        var count = await _cache.CountOnGoingMatches(GameMode.GM_1v1, GateWay.Undefined, "Overall", 0, null, "Echo Isles v2", CurrentNames);
        var byOldName = await _cache.CountOnGoingMatches(GameMode.GM_1v1, GateWay.Undefined, "Overall", 0, null, "Echo Isles 2.0", CurrentNames);

        Assert.That(matches.Select(m => m.MatchId), Is.EquivalentTo(new[] { "m1", "m2", "m4" }));
        Assert.That(count, Is.EqualTo(3));
        Assert.That(byOldName, Is.EqualTo(0));
    }

    [Test]
    public async Task MapName_WithoutCurrentNames_ComparesTheStoredName()
    {
        _cache.Upsert(Ongoing("m4", EchoIslesOldFile, 1051, "Echo Isles 2.0"));

        var count = await _cache.CountOnGoingMatches(GameMode.GM_1v1, GateWay.Undefined, "Overall", 0, null, "Echo Isles v2");
        var byOldName = await _cache.CountOnGoingMatches(GameMode.GM_1v1, GateWay.Undefined, "Overall", 0, null, "Echo Isles 2.0");

        Assert.That(count, Is.EqualTo(2));
        Assert.That(byOldName, Is.EqualTo(1));
    }

    [Test]
    public async Task MapName_OtherMode_KeepsTheStoredNameEvenWithCurrentNames()
    {
        _cache.Upsert(Ongoing("m5", LegionTdFile, 5072, "Legion TD 11.0", GameMode.GM_LEGION_1v1_x20));

        var byStoredName = await _cache.CountOnGoingMatches(GameMode.GM_LEGION_1v1_x20, GateWay.Undefined, "Overall", 0, null, "Legion TD 11.0", CurrentNames);
        var byCurrentName = await _cache.CountOnGoingMatches(GameMode.GM_LEGION_1v1_x20, GateWay.Undefined, "Overall", 0, null, "Legion TD: X3", CurrentNames);

        Assert.That(byStoredName, Is.EqualTo(1));
        Assert.That(byCurrentName, Is.EqualTo(0));
    }

    private static OnGoingMatchup Ongoing(string matchId, string map, int mapId, string mapName, GameMode gameMode = GameMode.GM_1v1)
    {
        return new OnGoingMatchup
        {
            Id = ObjectId.GenerateNewId(),
            MatchId = matchId,
            Map = map,
            MapId = mapId,
            MapName = mapName,
            GameMode = gameMode,
            GateWay = GateWay.Europe,
        };
    }
}
