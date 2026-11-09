using System.Linq;
using MongoDB.Bson;
using NUnit.Framework;
using W3ChampionsStatisticService.Tools.MapKeyBackfill;
using static WC3ChampionsStatisticService.Tests.Tools.MapKeyResolverTests;

namespace WC3ChampionsStatisticService.Tests.Tools;

public class MapKeyTransformTests
{
    private const string EchoFile = "1051_w3c_260615_2308_EchoIsles_v2.2.w3x";
    private const string EchoKey = "1w3c2606152308EchoIslesv2_2";
    private const string MeadowsFile = "w3c_250622_1057_TwistedMeadows_v1.9.w3x";
    private const string MeadowsKey = "2506221057TwistedMeadowsv1_9";

    private static MapKeyResolver Season25Resolver() => Resolver(new[]
    {
        Event("W3Champions/" + EchoFile, 1051, "Echo Isles v2", 25, OneVsOne, 27148),
        Event("W3Champions/" + MeadowsFile, 6, "Twisted Meadows", 25, OneVsOne, 900)
    });

    private static BsonArray Maps(BsonDocument doc, int gameMode)
    {
        return doc["MatchesOnMapPerModes"].AsBsonArray.Select(b => b.AsBsonDocument).Single(b => b["GameMode"] == gameMode)["Maps"].AsBsonArray;
    }

    [Test]
    public void MapsPerSeason_MigratesAndMergesAModeBucket_AndRebuildsTheAllModesBucket()
    {
        var doc = BsonDocument.Parse($$"""
            { "_id": "25", "Season": 25, "MatchesOnMapPerModes": [
              { "GameMode": 0, "Maps": [
                { "Map": "{{EchoKey}}", "Count": 30 },
                { "Map": "{{EchoFile}}", "MapId": 1051, "MapName": "Echo Isles v2", "Count": 5 },
                { "Map": "{{MeadowsKey}}", "Count": 7 } ] },
              { "GameMode": 1, "Maps": [
                { "Map": "{{EchoKey}}", "Count": 30 },
                { "Map": "{{EchoFile}}", "MapId": 1051, "MapName": "Echo Isles v2", "Count": 5 },
                { "Map": "{{MeadowsKey}}", "Count": 7 } ] } ] }
            """);
        var report = new MapKeyReport();

        var result = MapKeyTransforms.TransformMapsPerSeasonSeason(doc, Season25Resolver(), report);

        Assert.IsTrue(result.Changed);
        var expected = BsonArray.Create(new[]
        {
            BsonDocument.Parse($$"""{ "Map": "{{EchoFile}}", "MapId": 1051, "MapName": "Echo Isles v2", "Count": 35 }"""),
            BsonDocument.Parse($$"""{ "Map": "{{MeadowsFile}}", "MapId": 6, "MapName": "Twisted Meadows", "Count": 7 }""")
        });
        Assert.AreEqual(expected, Maps(result.Document, 1));
        Assert.AreEqual(expected, Maps(result.Document, 0));
        Assert.AreEqual(1, report.Rows.Single(r => r.LegacyKey == EchoKey).EntriesMerged);
        Assert.AreEqual(MapKeyStatus.Rebuilt, report.Rows.Single(r => r.LegacyKey == null).Status);
        Assert.AreEqual(1, report.TotalsFor(MapKeyTransforms.MapsPerSeason).EntriesAlreadyMigrated);

        Assert.IsFalse(MapKeyTransforms.TransformMapsPerSeasonSeason(result.Document, Season25Resolver(), new MapKeyReport()).Changed);
    }

    [Test]
    public void MapsPerSeason_AllModesBucket_KeepsAKeySplitByMode()
    {
        var doc = BsonDocument.Parse("""
            { "_id": "3", "Season": 3, "MatchesOnMapPerModes": [
              { "GameMode": 0, "Maps": [ { "Map": "twilightruins", "Count": 12 } ] },
              { "GameMode": 4, "Maps": [ { "Map": "twilightruins", "Count": 10 } ] },
              { "GameMode": 5, "Maps": [ { "Map": "twilightruins", "Count": 2 } ] } ] }
            """);

        var result = MapKeyTransforms.TransformMapsPerSeasonSeason(doc, Resolver(new EventMapGroup[0]), new MapKeyReport());

        Assert.AreEqual(BsonDocument.Parse("""{ "Map": "twilightruins", "MapId": 35, "MapName": "Twilight Ruins LV", "Count": 10 }"""), Maps(result.Document, 4)[0]);
        Assert.AreEqual(BsonDocument.Parse("""{ "Map": "twilightruins", "MapId": 19, "MapName": "Twilight Ruins", "Count": 2 }"""), Maps(result.Document, 5)[0]);
        var allModes = Maps(result.Document, 0);
        Assert.AreEqual(2, allModes.Count);
        CollectionAssert.AreEquivalent(new[] { 35, 19 }, allModes.Select(m => m["MapId"].AsInt32));
        Assert.AreEqual(12, allModes.Sum(m => m["Count"].AsInt32));
    }

    [Test]
    public void MapsPerSeason_AllModesBucketWithADifferentTotal_IsMigratedInPlace()
    {
        var doc = BsonDocument.Parse("""
            { "_id": "3", "Season": 3, "MatchesOnMapPerModes": [
              { "GameMode": 0, "Maps": [ { "Map": "twilightruins", "Count": 13 } ] },
              { "GameMode": 4, "Maps": [ { "Map": "twilightruins", "Count": 10 } ] },
              { "GameMode": 5, "Maps": [ { "Map": "twilightruins", "Count": 2 } ] } ] }
            """);
        var report = new MapKeyReport();

        var result = MapKeyTransforms.TransformMapsPerSeasonSeason(doc, Resolver(new EventMapGroup[0]), report);

        var allModes = Maps(result.Document, 0);
        Assert.AreEqual(1, allModes.Count);
        Assert.AreEqual(13, allModes[0]["Count"].AsInt32);
        Assert.AreEqual(35, allModes[0]["MapId"].AsInt32);
        Assert.AreEqual(MapKeyStatus.NotRebuilt, report.Rows.Single(r => r.LegacyKey == null).Status);
        Assert.IsTrue(report.Rows.Any(r => r.GameMode == 0 && r.Status == MapKeyStatus.AmbiguousMerged));
    }

    [Test]
    public void MapsPerSeason_AllSeasonsDocument_IsTheSumOfTheSeasons()
    {
        var resolver = Resolver(new EventMapGroup[0]);
        var season2 = MapKeyTransforms.TransformMapsPerSeasonSeason(BsonDocument.Parse("""
            { "_id": "2", "Season": 2, "MatchesOnMapPerModes": [
              { "GameMode": 0, "Maps": [ { "Map": "tidewaterglades", "Count": 10 } ] },
              { "GameMode": 4, "Maps": [ { "Map": "tidewaterglades", "Count": 10 } ] } ] }
            """), resolver, null).Document;
        var season3 = MapKeyTransforms.TransformMapsPerSeasonSeason(BsonDocument.Parse("""
            { "_id": "3", "Season": 3, "MatchesOnMapPerModes": [
              { "GameMode": 0, "Maps": [ { "Map": "tidewaterglades", "Count": 5 } ] },
              { "GameMode": 4, "Maps": [ { "Map": "tidewaterglades", "Count": 5 } ] } ] }
            """), resolver, null).Document;
        var allSeasons = BsonDocument.Parse("""
            { "_id": "-1", "Season": -1, "MatchesOnMapPerModes": [
              { "GameMode": 0, "Maps": [ { "Map": "tidewaterglades", "Count": 15 } ] },
              { "GameMode": 4, "Maps": [ { "Map": "tidewaterglades", "Count": 16 } ] } ] }
            """);
        var report = new MapKeyReport();

        var result = MapKeyTransforms.RebuildMapsPerSeasonAllSeasons(allSeasons, new[] { season2, season3 }, report);

        Assert.IsTrue(result.Changed);
        var allModes = Maps(result.Document, 0);
        Assert.AreEqual(BsonDocument.Parse("""{ "Map": "tidewaterglades", "MapId": 47, "MapName": "Tidewater Glades v2", "Count": 5 }"""), allModes[0]);
        Assert.AreEqual(BsonDocument.Parse("""{ "Map": "tidewaterglades", "MapName": "Tidewater Glades LV", "Count": 10 }"""), allModes[1]);

        // 4v4 stores 16 but the seasons sum to 15: rebuilt from the seasons anyway, and both totals reported.
        var fourVsFour = Maps(result.Document, 4);
        Assert.AreEqual(2, fourVsFour.Count);
        Assert.AreEqual(15, fourVsFour.Sum(e => e["Count"].AsInt32));
        var fourVsFourRow = report.Rows.Single(r => r.GameMode == 4);
        Assert.AreEqual(MapKeyStatus.Rebuilt, fourVsFourRow.Status);
        Assert.IsTrue(fourVsFourRow.TotalsDiffer);
        StringAssert.Contains("stored total was 16 (-1)", fourVsFourRow.Notes);

        var allModesRow = report.Rows.Single(r => r.GameMode == 0);
        Assert.AreEqual(MapKeyStatus.Rebuilt, allModesRow.Status);
        Assert.IsFalse(allModesRow.TotalsDiffer);

        Assert.IsFalse(MapKeyTransforms.RebuildMapsPerSeasonAllSeasons(result.Document, new[] { season2, season3 }, null).Changed);
    }

    [Test]
    public void MapsPerSeason_AllSeasonsDocument_GetsAModeOnlyTheSeasonsHave()
    {
        var resolver = Resolver(new EventMapGroup[0]);
        var season3 = MapKeyTransforms.TransformMapsPerSeasonSeason(BsonDocument.Parse("""
            { "_id": "3", "Season": 3, "MatchesOnMapPerModes": [
              { "GameMode": 0, "Maps": [ { "Map": "tidewaterglades", "Count": 5 } ] },
              { "GameMode": 4, "Maps": [ { "Map": "tidewaterglades", "Count": 5 } ] } ] }
            """), resolver, null).Document;
        var allSeasons = BsonDocument.Parse("""
            { "_id": "-1", "Season": -1, "MatchesOnMapPerModes": [
              { "GameMode": 0, "Maps": [ { "Map": "tidewaterglades", "Count": 5 } ] } ] }
            """);
        var report = new MapKeyReport();

        var result = MapKeyTransforms.RebuildMapsPerSeasonAllSeasons(allSeasons, new[] { season3 }, report);

        Assert.IsTrue(result.Changed);
        Assert.AreEqual(BsonDocument.Parse("""{ "Map": "tidewaterglades", "MapId": 47, "MapName": "Tidewater Glades v2", "Count": 5 }"""), Maps(result.Document, 4).Single());
        Assert.IsTrue(report.Rows.Single(r => r.GameMode == 4).TotalsDiffer);
        Assert.IsFalse(MapKeyTransforms.RebuildMapsPerSeasonAllSeasons(result.Document, new[] { season3 }, null).Changed);
    }

    [Test]
    public void PlayerHeroStats_MergesALegacyEntryIntoTheMigratedOne_AndKeepsOverallFirst()
    {
        // Saved by the new handler: the legacy entry got MapId: null, a later game added the file entry.
        var doc = BsonDocument.Parse($$"""
            { "_id": "25_player#1", "Season": 25, "BattleTag": "player#1", "HeroStatsItemList": [ { "HeroId": "archmage", "Stats": [ { "Race": 1, "WinLossesOnMap": [
              { "Map": "Overall", "MapId": null, "MapName": null, "WinLosses": [ { "Race": 2, "Wins": 3, "Losses": 1 } ] },
              { "Map": "{{EchoKey}}", "MapId": null, "MapName": null, "WinLosses": [ { "Race": 2, "Wins": 1, "Losses": 0 }, { "Race": 4, "Wins": 1, "Losses": 1 } ] },
              { "Map": "{{EchoFile}}", "MapId": 1051, "MapName": "Echo Isles v2", "WinLosses": [ { "Race": 2, "Wins": 2, "Losses": 1 } ] } ] } ] } ] }
            """);

        var result = MapKeyTransforms.TransformPlayerHeroStats(doc, Season25Resolver(), new MapKeyReport());

        var entries = result.Document["HeroStatsItemList"][0]["Stats"][0]["WinLossesOnMap"].AsBsonArray;
        Assert.AreEqual(2, entries.Count);
        Assert.AreEqual(doc["HeroStatsItemList"][0]["Stats"][0]["WinLossesOnMap"][0], entries[0]);
        Assert.AreEqual(BsonDocument.Parse($$"""
            { "Map": "{{EchoFile}}", "MapId": 1051, "MapName": "Echo Isles v2", "WinLosses": [ { "Race": 2, "Wins": 3, "Losses": 1 }, { "Race": 4, "Wins": 1, "Losses": 1 } ] }
            """), entries[1]);
        Assert.AreEqual("player#1", result.Document["BattleTag"].AsString);
        Assert.IsFalse(MapKeyTransforms.TransformPlayerHeroStats(result.Document, Season25Resolver(), null).Changed);
    }

    [Test]
    public void PlayerRaceOnMapVersusRaceRatio_MigratesDottedPatchKeysAndTheLegacyList()
    {
        var doc = BsonDocument.Parse($$"""
            { "_id": "25_player#1", "Season": 25, "RaceWinsOnMapByPatch": {
                "1.32.5": [ { "Race": 16, "WinLossesOnMap": [ { "Map": "{{MeadowsKey}}", "WinLosses": [ { "Race": 1, "Wins": 1, "Losses": 0 } ] } ] } ],
                "All": [ { "Race": 16, "WinLossesOnMap": [ { "Map": "Overall", "WinLosses": [] }, { "Map": "{{MeadowsKey}}", "WinLosses": [ { "Race": 1, "Wins": 1, "Losses": 0 } ] } ] } ] },
              "RaceWinsOnMap": [ { "Race": 1, "WinLossesOnMap": [ { "Map": "{{EchoKey}}", "WinLosses": [] } ] } ] }
            """);

        var result = MapKeyTransforms.TransformPlayerRaceOnMapVersusRaceRatio(doc, Season25Resolver(), new MapKeyReport());

        var byPatch = result.Document["RaceWinsOnMapByPatch"].AsBsonDocument;
        CollectionAssert.AreEqual(new[] { "1.32.5", "All" }, byPatch.Names);
        Assert.AreEqual(MeadowsFile, byPatch["1.32.5"][0]["WinLossesOnMap"][0]["Map"].AsString);
        Assert.AreEqual(6, byPatch["1.32.5"][0]["WinLossesOnMap"][0]["MapId"].AsInt32);
        Assert.AreEqual("Overall", byPatch["All"][0]["WinLossesOnMap"][0]["Map"].AsString);
        Assert.AreEqual(MeadowsFile, byPatch["All"][0]["WinLossesOnMap"][1]["Map"].AsString);
        Assert.AreEqual(EchoFile, result.Document["RaceWinsOnMap"][0]["WinLossesOnMap"][0]["Map"].AsString);
    }

    [Test]
    public void OverallRaceAndWinStat_MigratesLegacyEntries_KeepsOverallFirst_AndLeavesUnresolvedOnes()
    {
        var doc = BsonDocument.Parse($$"""
            { "_id": 0, "MmrRange": 0, "PatchToStatsPerModes": { "2.0.3": [
                { "MapName": "Overall", "Ratio": [ { "Race": 1, "WinLosses": [ { "Race": 2, "Wins": 1, "Losses": 0 } ] } ] },
                { "MapName": "{{EchoKey}}", "Ratio": [ { "Race": 1, "WinLosses": [ { "Race": 2, "Wins": 1, "Losses": 0 } ] } ] },
                { "Map": null, "MapId": null, "MapName": "zzzunknown", "Ratio": [] },
                { "Map": "{{EchoFile}}", "MapId": 1051, "MapName": "Echo Isles v2", "Ratio": [ { "Race": 1, "WinLosses": [ { "Race": 2, "Wins": 2, "Losses": 3 } ] }, { "Race": 2, "WinLosses": [] } ] } ] },
              "StatsPerModes": [ { "Map": null, "MapId": null, "MapName": "autumnleaves", "Ratio": [] } ] }
            """);

        var result = MapKeyTransforms.TransformOverallRaceAndWinStat(doc, Season25Resolver(), new MapKeyReport());

        var list = result.Document["PatchToStatsPerModes"]["2.0.3"].AsBsonArray;
        Assert.AreEqual(3, list.Count);
        Assert.AreEqual(BsonDocument.Parse("""{ "Map": "Overall", "MapName": "Overall", "Ratio": [ { "Race": 1, "WinLosses": [ { "Race": 2, "Wins": 1, "Losses": 0 } ] } ] }"""), list[0]);
        Assert.AreEqual(BsonDocument.Parse($$"""
            { "Map": "{{EchoFile}}", "MapId": 1051, "MapName": "Echo Isles v2", "Ratio": [ { "Race": 1, "WinLosses": [ { "Race": 2, "Wins": 3, "Losses": 3 } ] }, { "Race": 2, "WinLosses": [] } ] }
            """), list[1]);
        Assert.AreEqual(doc["PatchToStatsPerModes"]["2.0.3"][2], list[2]);
        Assert.AreEqual(BsonDocument.Parse("""{ "Map": "autumnleaves", "MapId": null, "MapName": "Autumn Leaves", "Ratio": [] }"""), result.Document["StatsPerModes"][0]);
        Assert.IsFalse(MapKeyTransforms.TransformOverallRaceAndWinStat(result.Document, Season25Resolver(), null).Changed);
    }

    [Test]
    public void Matchup_TakesTheFileOfItsEvent_AndKeepsStoredIdAndName()
    {
        var doc = BsonDocument.Parse($$"""{ "_id": 1, "Map": "{{EchoKey}}", "MapId": 1051, "MapName": "Echo Isles v1", "Season": 25, "GameMode": 1, "MatchId": "m1" }""");

        var set = MapKeyTransforms.MatchupUpdate(doc, new JoinedEventMap("W3Champions/" + EchoFile, 1051, "Echo Isles v2"), Season25Resolver(), new MapKeyReport());

        Assert.AreEqual(BsonDocument.Parse($$"""{ "Map": "{{EchoFile}}" }"""), set);
    }

    [Test]
    public void Matchup_AlreadyMigrated_IsSkipped()
    {
        var doc = BsonDocument.Parse($$"""{ "_id": 1, "Map": "{{EchoFile}}", "Season": 25, "GameMode": 1 }""");

        Assert.IsNull(MapKeyTransforms.MatchupUpdate(doc, null, Season25Resolver(), new MapKeyReport()));
    }

    [Test]
    public void Matchup_WithoutEvent_InASeasonWithoutEvents_KeepsTheKeyAndTakesTheManualIdAndName()
    {
        var doc = BsonDocument.Parse("""{ "_id": 1, "Map": "lastrefuge", "MapName": null, "Season": 5, "GameMode": 1 }""");

        var set = MapKeyTransforms.MatchupUpdate(doc, null, Season25Resolver(), new MapKeyReport());

        Assert.AreEqual(BsonDocument.Parse("""{ "MapId": 3, "MapName": "Last Refuge" }"""), set);
    }

    [Test]
    public void Matchup_PreGapEventWithoutName_FillsTheNameFromTheManualMap()
    {
        var doc = BsonDocument.Parse("""{ "_id": 1, "Map": "lastrefuge", "MapId": 3, "Season": 0, "GameMode": 1 }""");
        var report = new MapKeyReport();

        var set = MapKeyTransforms.MatchupUpdate(doc, new JoinedEventMap("Maps/frozenthrone/community/(2)lastrefuge.w3x", 3, null), Season25Resolver(), report);

        Assert.AreEqual(BsonDocument.Parse("""{ "Map": "(2)lastrefuge.w3x", "MapName": "Last Refuge" }"""), set);
        Assert.AreEqual(MapKeyStatus.ResolvedManual, report.Rows.Single().Status);
    }
}
