using System.Collections.Generic;
using System.IO;
using System.Linq;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using NUnit.Framework;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Tools.MapKeyBackfill;

namespace WC3ChampionsStatisticService.Tests.Tools;

// The fixture events in Tools/fixtures are imported into a local MongoDB to produce legacy-shaped read models
// with the old handlers, and then migrated with backfill-map-keys. These tests keep the cases they are meant
// to cover from drifting.
public class MapKeyFixtureTests
{
    private const int OneVsOne = 1;
    private const int TwoVsTwo = 2;
    private const int Legion4v4 = 202;
    private const int Legion1v1 = 203;

    private const string EchoIslesA = "1051_w3c_260615_2308_EchoIsles_v2.2.w3x";
    private const string OasisLv = "w3c_1v1_fixtureoasis_lv.w3x";
    private const string OasisAnon = "w3c_1v1_fixtureoasis.anon.w3x";
    private const string GroveLv = "w3c_1v1_fixturegrove_lv.w3x";
    private const string LegionKey = "ionTD11_4bpracmiW3CTeamOZE";

    private static readonly string FixtureDir = Path.Combine(TestContext.CurrentContext.TestDirectory, "Tools", "fixtures");

    private static List<BsonDocument> LoadDocuments(string name)
    {
        var array = BsonSerializer.Deserialize<BsonArray>(File.ReadAllText(Path.Combine(FixtureDir, name)));
        return array.Select(v => v.AsBsonDocument).ToList();
    }

    private static List<MatchFinishedEvent> LoadEvents(string name)
    {
        return LoadDocuments(name).Select(d => BsonSerializer.Deserialize<MatchFinishedEvent>(d)).ToList();
    }

    // Same grouping as the event catalog: a missing season counts as 0.
    private static List<EventMapGroup> Catalog(IEnumerable<MatchFinishedEvent> events)
    {
        return events
            .GroupBy(e => (e.match.map, e.match.mapId, e.match.mapName, e.match.season, GameMode: (int)e.match.gameMode))
            .Select(g => new EventMapGroup(g.Key.map, g.Key.mapId, g.Key.mapName, g.Key.season, g.Key.GameMode, g.Count(), g.Max(e => e.Id)))
            .ToList();
    }

    private static MapKeyResolver Resolver()
    {
        var mmNames = new Dictionary<int, string> { [6] = "Twisted Meadows" };
        return new MapKeyResolver(Catalog(LoadEvents("map-key-events.json")), mmNames: mmNames);
    }

    [TestCase("map-key-events.json")]
    [TestCase("map-key-events-extra.json")]
    public void Fixtures_DeserializeAsFinishedMatches(string name)
    {
        var events = LoadEvents(name);

        Assert.IsNotEmpty(events);
        Assert.AreEqual(events.Count, events.Select(e => e.Id).Distinct().Count());
        Assert.AreEqual(events.Count, events.Select(e => e.match.id).Distinct().Count());
        Assert.That(events.Select(e => e.Id), Is.Ordered);
        foreach (var e in events)
        {
            Assert.IsNotNull(e.match.map);
            Assert.AreEqual(e.match.players.Count, e.result.players.Count);
            Assert.IsTrue(e.match.players.All(p => p.battleTag.StartsWith("Fixture")));
        }
    }

    [Test]
    public void ExtraFixtures_ComeAfterTheMainOnes()
    {
        var lastMain = LoadEvents("map-key-events.json").Max(e => e.Id);
        var extra = LoadEvents("map-key-events-extra.json");

        Assert.IsTrue(extra.All(e => e.Id > lastMain));
        Assert.IsTrue(extra.All(e => e.match.season == 25));
    }

    [Test]
    public void Fixtures_CoverSeveralSeasonsAndAnEventWithoutNameOrSeason()
    {
        var documents = LoadDocuments("map-key-events.json");
        var events = LoadEvents("map-key-events.json");

        Assert.That(events.Select(e => e.match.season).Distinct(), Is.EquivalentTo(new[] { 0, 23, 25 }));
        Assert.IsTrue(documents.Any(d => !d["match"].AsBsonDocument.Contains("mapName") && !d["match"].AsBsonDocument.Contains("season")));
    }

    [Test]
    public void EchoIslesFiles_HaveDifferentLegacyKeys_AndResolveToTheirOwnFile()
    {
        var resolver = Resolver();
        var files = LoadEvents("map-key-events.json").Where(e => e.match.mapId == 1051).Select(e => MapFileName.Of(e.match.map)).Distinct().ToList();

        Assert.AreEqual(3, files.Count);
        Assert.AreEqual(3, files.Select(LegacyMapKey.Compute).Distinct().Count());
        foreach (var file in files)
        {
            var resolution = resolver.Resolve(MapKeyScope.SeasonAndMode, LegacyMapKey.Compute(file), 25, OneVsOne);
            Assert.AreEqual(MapKeyStatus.Resolved, resolution.Status);
            Assert.AreEqual(file, resolution.Map);
            Assert.AreEqual(1051, resolution.MapId);
            Assert.AreEqual("Echo Isles v2", resolution.MapName);
        }

        Assert.AreEqual(MapKeyStatus.Migrated, resolver.Resolve(MapKeyScope.SeasonAndMode, EchoIslesA, 25, OneVsOne).Status);
    }

    [Test]
    public void LvAndAnonFiles_OfTwoMaps_AreMergedToTheMorePlayed()
    {
        Assert.AreEqual(LegacyMapKey.Compute(OasisLv), LegacyMapKey.Compute(OasisAnon));

        var resolution = Resolver().Resolve(MapKeyScope.SeasonAndMode, LegacyMapKey.Compute(OasisLv), 25, OneVsOne);

        Assert.AreEqual(MapKeyStatus.AmbiguousMerged, resolution.Status);
        Assert.AreEqual(OasisLv, resolution.Map);
        Assert.AreEqual(990001, resolution.MapId);
        Assert.AreEqual(2, resolution.CandidateFiles.Count);
    }

    [Test]
    public void LvAndAnonFiles_OfOneMap_ResolveToTheMorePlayed()
    {
        var resolution = Resolver().Resolve(MapKeyScope.SeasonAndMode, LegacyMapKey.Compute(GroveLv), 25, OneVsOne);

        Assert.AreEqual(MapKeyStatus.Resolved, resolution.Status);
        Assert.AreEqual(GroveLv, resolution.Map);
        Assert.AreEqual(990003, resolution.MapId);
        Assert.AreEqual(2, resolution.CandidateFiles.Count);
    }

    [Test]
    public void EventWithoutName_TakesTheFileFromEvents_AndTheNameFromMatchmaking()
    {
        var resolution = Resolver().Resolve(MapKeyScope.SeasonAndMode, "twistedmeadows", 0, OneVsOne);

        Assert.AreEqual(MapKeyStatus.ResolvedByCatalog, resolution.Status);
        Assert.AreEqual("(4)twistedmeadows.w3x", resolution.Map);
        Assert.AreEqual(6, resolution.MapId);
        Assert.AreEqual("Twisted Meadows", resolution.MapName);
    }

    [Test]
    public void RenamedMap_KeepsItsStoredName()
    {
        var file = "w3c_251004_2004_TurtleRock_v2.0.w3x";

        var resolution = Resolver().Resolve(MapKeyScope.SeasonAndMode, LegacyMapKey.Compute(file), 23, TwoVsTwo);

        Assert.AreEqual(MapKeyStatus.Resolved, resolution.Status);
        Assert.AreEqual(file, resolution.Map);
        Assert.AreEqual(12, resolution.MapId);
        Assert.AreEqual("Turtle Rock", resolution.MapName);
    }

    [Test]
    public void LegionKey_SplitsByMode_AndMergesWithoutOne()
    {
        var resolver = Resolver();

        Assert.AreEqual(5546, resolver.Resolve(MapKeyScope.SeasonAndMode, LegionKey, 25, Legion1v1).MapId);
        Assert.AreEqual(5459, resolver.Resolve(MapKeyScope.SeasonAndMode, LegionKey, 25, Legion4v4).MapId);

        var noMode = resolver.Resolve(MapKeyScope.SeasonOnly, LegionKey, 25, null);
        Assert.AreEqual(MapKeyStatus.AmbiguousMerged, noMode.Status);
        Assert.AreEqual(5546, noMode.MapId);
    }
}
