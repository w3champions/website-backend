using System;
using System.Linq;
using NUnit.Framework;
using W3C.Contracts.Matchmaking;
using W3C.Domain.GameModes;
using W3ChampionsStatisticService.Tools.MapKeyBackfill;

namespace WC3ChampionsStatisticService.Tests.Tools;

public class MapKeyBackfillOptionsTests
{
    private const string Local = "mongodb://localhost:27017";
    private const string Remote = "mongodb://w3champions.com:3511";

    [Test]
    [TestCase("path/w3c_1v1_autumnleaves_anon", "autumnleaves")]
    [TestCase("path/w3c_1v1_terenasstand_anon", "terenasstand")]
    [TestCase("path/w3c_gnollwood_anon", "gnollwood")]
    [TestCase("path/w3c_tidewaterglades_lv_anon", "tidewaterglades")]
    [TestCase("path/w3c_tidewaterglades_anon", "tidewaterglades")]
    [TestCase("path/w3c_ffa_marketsquare_anon_cd", "marketsquare")]
    [TestCase("path/w3c_ffa_marketsquare_cd", "marketsquare")]
    [TestCase("path/w3c_1v1_lastrefuge.anon.w3x", "lastrefuge")]
    [TestCase("W3Champions/1051_w3c_260615_2308_EchoIsles_v2.2.w3x", "1w3c2606152308EchoIslesv2_2")]
    [TestCase("W3Champions\\w3c_250622_1057_ShallowGrave_v1.5.w3x", "2506221057ShallowGravev1_5")]
    public void LegacyMapKey_MatchesTheOldMangling(string path, string expected)
    {
        Assert.AreEqual(expected, LegacyMapKey.Compute(path));
    }

    [Test]
    [TestCase(null)]
    [TestCase("path/ab")]
    [TestCase("path/ab.w3x")]
    public void LegacyMapKey_ReturnsNullWhereNoKeyWasStored(string path)
    {
        Assert.IsNull(LegacyMapKey.Compute(path));
    }

    [Test]
    public void GameModeNames_MatchTheGameModeEnum()
    {
        var values = Enum.GetValues<GameMode>();

        Assert.AreEqual(values.Length, GameModeNames.ValuesByName.Count);
        foreach (var value in values)
        {
            Assert.AreEqual((int)value, GameModeNames.Parse(value.ToString()), value.ToString());
        }
    }

    [Test]
    public void GameModeNames_CurrentMapNameModes_MatchTheBackend()
    {
        Assert.That(GameModeNames.CurrentMapNameModes, Is.EquivalentTo(GameModesHelper.CurrentMapNameGameModes.Select(m => (int)m)));
    }

    [Test]
    public void Parse_Defaults()
    {
        var options = MapKeyBackfillOptions.Parse(new[] { "backfill-map-keys", "--connection-string", Local });

        Assert.IsFalse(options.Apply);
        CollectionAssert.AreEqual(MapKeyBackfillOptions.SupportedCollections, options.Collections);
        Assert.AreEqual(MapKeyBackfillOptions.DefaultAggregationPauseMs, options.AggregationPauseMs);
        Assert.AreEqual(0, options.BatchPauseMs);
        Assert.IsNull(options.EventCatalogPath);
    }

    [Test]
    [TestCase("--collections", "OnGoingMatchup")]
    [TestCase("--collections", "Matchup,Unknown")]
    [TestCase("--collections", "Matchup,MapsPerSeason", "--start-after", "6293e4f47c3d80a48ad1a6fe")]
    [TestCase("--collections", "MapsPerSeason", "--start-after", "5")]
    [TestCase("--collections", "OverallRaceAndWinStat", "--start-after", "1000")]
    [TestCase("--collections", "Matchup", "--start-after", "not-an-object-id")]
    [TestCase("--allow-remote")]
    [TestCase("--confirm-database", "W3Champions-Statistic-Service")]
    [TestCase("--event-catalog", "a.jsonl", "--write-event-catalog", "b.jsonl")]
    [TestCase("--batch-size", "0")]
    [TestCase("--batch-pause-ms", "-1")]
    public void Parse_RejectsInvalidOptions(params string[] extra)
    {
        var args = new[] { "backfill-map-keys", "--connection-string", Local }.Concat(extra).ToArray();

        Assert.Throws<ArgumentException>(() => MapKeyBackfillOptions.Parse(args));
    }

    [Test]
    public void Parse_AcceptsStartAfterForOnePagedCollection()
    {
        var options = MapKeyBackfillOptions.Parse(new[] { "--connection-string", Local, "--collections", "PlayerHeroStats", "--start-after", "25_player#1234" });

        Assert.AreEqual("25_player#1234", options.StartAfter);
    }

    [Test]
    [TestCase("mongodb://localhost:27017", true)]
    [TestCase("mongodb://127.0.0.1:27017/?directConnection=true", true)]
    [TestCase("mongodb://localhost:27017,w3champions.com:3511", false)]
    [TestCase("mongodb://w3champions.com:3511", false)]
    [TestCase("mongodb+srv://cluster.example.net", false)]
    public void DescribeTarget_IsLocalOnlyWhenEveryHostIsLocal(string connectionString, bool expected)
    {
        Assert.AreEqual(expected, MapKeyBackfillSafetyGuard.DescribeTarget(connectionString).IsLocal);
    }

    [Test]
    public void Guard_RefusesRemoteApplyWithoutAllowRemote()
    {
        var options = MapKeyBackfillOptions.Parse(new[] { "--connection-string", Remote, "--apply" });

        Assert.Throws<ArgumentException>(() => MapKeyBackfillSafetyGuard.EnsureApplyAllowed(options, MapKeyBackfillSafetyGuard.DescribeTarget(Remote)));
    }

    [Test]
    public void Guard_RefusesAWrongConfirmDatabase()
    {
        var options = MapKeyBackfillOptions.Parse(new[] { "--connection-string", Remote, "--apply", "--allow-remote", "--confirm-database", "W3Champions" });

        Assert.Throws<ArgumentException>(() => MapKeyBackfillSafetyGuard.EnsureApplyAllowed(options, MapKeyBackfillSafetyGuard.DescribeTarget(Remote)));
    }

    [Test]
    public void Guard_AllowsLocalApplyAndConfirmedRemoteApply()
    {
        var local = MapKeyBackfillOptions.Parse(new[] { "--connection-string", Local, "--apply" });
        var remote = MapKeyBackfillOptions.Parse(new[] { "--connection-string", Remote, "--apply", "--allow-remote", "--confirm-database", MapKeyBackfillOptions.DefaultDatabaseName });

        Assert.DoesNotThrow(() => MapKeyBackfillSafetyGuard.EnsureApplyAllowed(local, MapKeyBackfillSafetyGuard.DescribeTarget(Local)));
        Assert.DoesNotThrow(() => MapKeyBackfillSafetyGuard.EnsureApplyAllowed(remote, MapKeyBackfillSafetyGuard.DescribeTarget(Remote)));
    }

    [Test]
    public void ManualMap_ParsesSeasonsAndGameModes()
    {
        var manual = ManualMap.Parse("""
            [
              { "mapId": 35, "mapName": "Twilight Ruins LV", "keys": ["twilightruins"], "seasons": "1-12", "gameModes": ["GM_4v4"], "games": 13231, "idFrom": "x" },
              { "mapId": 19, "mapName": "Twilight Ruins", "keys": ["twilightruins"], "seasons": "1-12", "gameModes": ["FFA"], "games": 954 },
              { "mapId": 3, "mapName": "Last Refuge", "keys": ["LastRefuge", "lastrefuge"], "seasons": "0-11,24" }
            ]
            """);

        Assert.AreEqual(35, manual.Match("twilightruins", 3, GameModeNames.Parse("GM_4v4")).Single().MapId);
        Assert.AreEqual(19, manual.Match("twilightruins", 3, GameModeNames.Parse("FFA")).Single().MapId);
        Assert.AreEqual(2, manual.Match("twilightruins", 3, null).Count);
        Assert.AreEqual(0, manual.Match("twilightruins", 13, null).Count);
        Assert.AreEqual(1, manual.Match("lastrefuge", 24, GameModeNames.OneVsOne).Count);
        Assert.AreEqual(0, manual.Match("lastrefuge", 12, GameModeNames.OneVsOne).Count);
        Assert.AreEqual(0, manual.Match("LASTREFUGE", 5, GameModeNames.OneVsOne).Count);
        Assert.AreEqual(1, manual.Match("LastRefuge", null, null).Count);
    }

    [Test]
    [TestCase("7-16,18-25", 7, 25, 17)]
    [TestCase("10", 10, 10, 11)]
    public void ManualMap_ParseSeasons(string text, int first, int last, int absent)
    {
        var seasons = ManualMap.ParseSeasons(text);

        Assert.IsTrue(seasons.Contains(first));
        Assert.IsTrue(seasons.Contains(last));
        Assert.IsFalse(seasons.Contains(absent));
    }

    [Test]
    [TestCase("5-3")]
    [TestCase("a-b")]
    [TestCase("1-2-3")]
    public void ManualMap_RejectsInvalidSeasons(string text)
    {
        Assert.Throws<FormatException>(() => ManualMap.ParseSeasons(text));
    }

    [Test]
    public void ManualMap_RejectsUnknownGameModes()
    {
        Assert.Throws<FormatException>(() => ManualMap.Parse("""[{ "mapId": 1, "mapName": "Concealed Hill", "keys": ["ConcealedHill"], "seasons": "6", "gameModes": ["GM_9v9"] }]"""));
    }
}
