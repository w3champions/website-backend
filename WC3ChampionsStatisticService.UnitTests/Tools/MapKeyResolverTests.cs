using System.Collections.Generic;
using MongoDB.Bson;
using NUnit.Framework;
using W3ChampionsStatisticService.Tools.MapKeyBackfill;

namespace WC3ChampionsStatisticService.Tests.Tools;

public class MapKeyResolverTests
{
    public const int OneVsOne = 1;
    public const int FourVsFour = 4;
    public const int Ffa = 5;
    public const int Legion4v4 = 202;
    public const int Legion1v1 = 203;

    public const string ManualJson = """
        [
          { "mapId": null, "mapName": "Tidewater Glades LV", "keys": ["tidewaterglades", "_tidewaterglades_anon"], "seasons": "1-2", "games": 4174 },
          { "mapId": 47, "mapName": "Tidewater Glades v2", "keys": ["tidewaterglades"], "seasons": "3-11", "games": 30000 },
          { "mapId": 35, "mapName": "Twilight Ruins LV", "keys": ["twilightruins"], "seasons": "1-12", "gameModes": ["GM_4v4"], "games": 13231 },
          { "mapId": 19, "mapName": "Twilight Ruins", "keys": ["twilightruins"], "seasons": "1-12", "gameModes": ["FFA"], "games": 954 },
          { "mapId": null, "mapName": "Autumn Leaves", "keys": ["AutumnLeaves", "autumnleaves"], "seasons": "2-8", "games": 115589 },
          { "mapId": 44, "mapName": "Autumn Leaves v2", "keys": ["AutumnLeavesv2-0"], "seasons": "9-11", "games": 50000 },
          { "mapId": 3, "mapName": "Last Refuge", "keys": ["LastRefuge", "lastrefuge"], "seasons": "0-10", "games": 427560 }
        ]
        """;

    private static int _sequence;

    public static EventMapGroup Event(string path, int? mapId, string mapName, int season, int gameMode, long count)
    {
        // Later calls get later ids, so the order of the calls is the order of the events.
        return new EventMapGroup(path, mapId, mapName, season, gameMode, count, ObjectId.GenerateNewId(1600000000 + _sequence++));
    }

    public static MapKeyResolver Resolver(IEnumerable<EventMapGroup> events, IReadOnlyDictionary<string, int> mapData = null, IReadOnlyDictionary<int, string> mmMaps = null)
    {
        return new MapKeyResolver(events, mapData, mmMaps, ManualMap.Parse(ManualJson));
    }

    [Test]
    public void KnownFileName_IsAlreadyMigrated()
    {
        var resolver = Resolver(new[] { Event("W3Champions/1051_w3c_260615_2308_EchoIsles_v2.2.w3x", 1051, "Echo Isles v2", 25, OneVsOne, 27148) }, new Dictionary<string, int> { ["w3c_250622_1057_TwistedMeadows_v1.9.w3x"] = 6 });

        Assert.AreEqual(MapKeyStatus.Migrated, resolver.Resolve(MapKeyScope.SeasonAndMode, "1051_w3c_260615_2308_EchoIsles_v2.2.w3x", 25, OneVsOne).Status);
        Assert.AreEqual(MapKeyStatus.Migrated, resolver.Resolve(MapKeyScope.SeasonOnly, "w3c_250622_1057_TwistedMeadows_v1.9.w3x", 25, null).Status);
    }

    [Test]
    public void SameSeasonEvent_GivesFileIdAndName()
    {
        var resolver = Resolver(new[] { Event("W3Champions/1051_w3c_260615_2308_EchoIsles_v2.2.w3x", 1051, "Echo Isles v2", 25, OneVsOne, 27148) });

        var resolution = resolver.Resolve(MapKeyScope.SeasonAndMode, "1w3c2606152308EchoIslesv2_2", 25, OneVsOne);

        Assert.AreEqual(MapKeyStatus.Resolved, resolution.Status);
        Assert.AreEqual("1051_w3c_260615_2308_EchoIsles_v2.2.w3x", resolution.Map);
        Assert.AreEqual(1051, resolution.MapId);
        Assert.AreEqual("Echo Isles v2", resolution.MapName);
    }

    [Test]
    public void EventFromAnotherSeasonOrMode_GivesNoFile()
    {
        var resolver = Resolver(new[] { Event("W3Champions/1051_w3c_260615_2308_EchoIsles_v2.2.w3x", 1051, "Echo Isles v2", 25, OneVsOne, 27148) });

        // Same season, other mode: the key is kept; the metadata fallback may still give id and name.
        var otherMode = resolver.Resolve(MapKeyScope.SeasonAndMode, "1w3c2606152308EchoIslesv2_2", 25, FourVsFour);
        Assert.AreEqual(MapKeyStatus.ResolvedByCatalog, otherMode.Status);
        Assert.AreEqual("1w3c2606152308EchoIslesv2_2", otherMode.Map);
        Assert.AreEqual(1051, otherMode.MapId);

        // A season without events: no file, no fallback.
        var otherSeason = resolver.Resolve(MapKeyScope.SeasonAndMode, "1w3c2606152308EchoIslesv2_2", 5, OneVsOne);
        Assert.AreEqual(MapKeyStatus.LegacyKey, otherSeason.Status);
        Assert.AreEqual("1w3c2606152308EchoIslesv2_2", otherSeason.Map);
        Assert.IsNull(otherSeason.MapId);
    }

    [Test]
    public void PreGapEventWithMapOnly_TakesItsNameFromMmMaps()
    {
        var resolver = Resolver(
            new[] { Event("Maps/frozenthrone/community/(2)concealedhill.w3x", 1, null, 0, OneVsOne, 13331) },
            mmMaps: new Dictionary<int, string> { [1] = "Concealed Hill" });

        var resolution = resolver.Resolve(MapKeyScope.SeasonAndMode, "concealedhill", 0, OneVsOne);

        Assert.AreEqual(MapKeyStatus.ResolvedByCatalog, resolution.Status);
        Assert.AreEqual("(2)concealedhill.w3x", resolution.Map);
        Assert.AreEqual(1, resolution.MapId);
        Assert.AreEqual("Concealed Hill", resolution.MapName);
    }

    [Test]
    public void EventWithoutId_TakesItsIdFromMapData()
    {
        var resolver = Resolver(
            new[] { Event("W3Champions/w3c_250622_1057_TwistedMeadows_v1.9.w3x", null, "Twisted Meadows", 22, OneVsOne, 10) },
            new Dictionary<string, int> { ["w3c_250622_1057_TwistedMeadows_v1.9.w3x"] = 6 });

        var resolution = resolver.Resolve(MapKeyScope.SeasonAndMode, "2506221057TwistedMeadowsv1_9", 22, OneVsOne);

        Assert.AreEqual(MapKeyStatus.ResolvedByCatalog, resolution.Status);
        Assert.AreEqual(6, resolution.MapId);
        Assert.AreEqual("events+MapData", resolution.Source);
    }

    [Test]
    public void ManualEntry_WinsOverTheEventName_AndKeepsTheEventFile()
    {
        var resolver = Resolver(new[] { Event("W3Champions/v10/w3c_AutumnLeaves_v2-0.w3x", 44, "Autumn Leaves", 11, OneVsOne, 35803) });

        var resolution = resolver.Resolve(MapKeyScope.SeasonAndMode, "AutumnLeavesv2-0", 11, OneVsOne);

        Assert.AreEqual(MapKeyStatus.ResolvedManual, resolution.Status);
        Assert.AreEqual("w3c_AutumnLeaves_v2-0.w3x", resolution.Map);
        Assert.AreEqual(44, resolution.MapId);
        Assert.AreEqual("Autumn Leaves v2", resolution.MapName);
        StringAssert.Contains("Autumn Leaves", resolution.Notes);
    }

    [Test]
    public void KeySplitBySeason_TakesTheEntryOfItsSeason()
    {
        var resolver = Resolver(new[] { Event("W3Champions/1051_w3c_260615_2308_EchoIsles_v2.2.w3x", 1051, "Echo Isles v2", 25, OneVsOne, 1) });

        var lv = resolver.Resolve(MapKeyScope.SeasonAndMode, "tidewaterglades", 2, FourVsFour);
        var v2 = resolver.Resolve(MapKeyScope.SeasonAndMode, "tidewaterglades", 3, FourVsFour);

        Assert.AreEqual(MapKeyStatus.LegacyKey, lv.Status);
        Assert.AreEqual("tidewaterglades", lv.Map);
        Assert.IsNull(lv.MapId);
        Assert.AreEqual("Tidewater Glades LV", lv.MapName);
        Assert.AreEqual(MapKeyStatus.LegacyKey, v2.Status);
        Assert.AreEqual(47, v2.MapId);
        Assert.AreEqual("Tidewater Glades v2", v2.MapName);
    }

    [Test]
    public void KeySplitByMode_TakesTheEntryOfItsMode()
    {
        var resolver = Resolver(new EventMapGroup[0]);

        Assert.AreEqual(35, resolver.Resolve(MapKeyScope.SeasonAndMode, "twilightruins", 3, FourVsFour).MapId);
        Assert.AreEqual(19, resolver.Resolve(MapKeyScope.SeasonAndMode, "twilightruins", 3, Ffa).MapId);

        var withoutMode = resolver.Resolve(MapKeyScope.SeasonOnly, "twilightruins", 3, null);
        Assert.AreEqual(MapKeyStatus.AmbiguousMerged, withoutMode.Status);
        Assert.AreEqual(35, withoutMode.MapId);
    }

    [Test]
    public void EntryWithoutId_KeepsItsFixedName()
    {
        var resolution = Resolver(new EventMapGroup[0]).Resolve(MapKeyScope.SeasonOnly, "autumnleaves", 5, null);

        Assert.AreEqual(MapKeyStatus.LegacyKey, resolution.Status);
        Assert.AreEqual("autumnleaves", resolution.Map);
        Assert.IsNull(resolution.MapId);
        Assert.AreEqual("Autumn Leaves", resolution.MapName);
    }

    [Test]
    public void KeyInASeasonWithoutEvents_AndNoManualEntry_KeepsTheKeyWithoutName()
    {
        var resolver = Resolver(new[] { Event("W3Champions/1051_w3c_260615_2308_EchoIsles_v2.2.w3x", 1051, "Echo Isles v2", 25, OneVsOne, 1) });

        var resolution = resolver.Resolve(MapKeyScope.SeasonAndMode, "LegionTD", 6, Legion4v4);

        Assert.AreEqual(MapKeyStatus.LegacyKey, resolution.Status);
        Assert.AreEqual("LegionTD", resolution.Map);
        Assert.IsNull(resolution.MapId);
        Assert.IsNull(resolution.MapName);
        Assert.IsTrue(resolution.ChangesEntry);
    }

    [Test]
    public void OlderManglingInASeasonWithEvents_FallsBackToTheMetadataResolver()
    {
        var resolver = Resolver(new[] { Event("W3Champions/w3c_250622_1057_ShallowGrave_v1.5.w3x", 61, "Shallow Grave", 22, OneVsOne, 100) });

        var resolution = resolver.Resolve(MapKeyScope.SeasonAndMode, "ShallowGravev1_4", 22, OneVsOne);

        Assert.AreEqual(MapKeyStatus.ResolvedByCatalog, resolution.Status);
        Assert.AreEqual("ShallowGravev1_4", resolution.Map);
        Assert.AreEqual(61, resolution.MapId);
        StringAssert.StartsWith("resolver:", resolution.Source);
    }

    [Test]
    public void UnknownKeyInASeasonWithEvents_IsUnresolved()
    {
        var resolver = Resolver(new[] { Event("W3Champions/w3c_250622_1057_ShallowGrave_v1.5.w3x", 61, "Shallow Grave", 22, OneVsOne, 100) });

        var resolution = resolver.Resolve(MapKeyScope.SeasonAndMode, "zzzunknown", 22, OneVsOne);

        Assert.AreEqual(MapKeyStatus.Unresolved, resolution.Status);
        Assert.IsFalse(resolution.ChangesEntry);
    }

    [Test]
    public void OneIdUnderTwoNames_IsOneMap_WithTheLatestName()
    {
        var resolver = Resolver(new[]
        {
            Event("W3Champions/v10/w3c_AutumnLeaves.w3x", 44, "Autumn Leaves", 14, OneVsOne, 900),
            Event("W3Champions/v10/w3c_AutumnLeaves.w3x", 44, "Autumn Leaves v2", 14, OneVsOne, 100)
        });

        var resolution = resolver.Resolve(MapKeyScope.SeasonAndMode, "AutumnLeaves", 14, OneVsOne);

        Assert.AreEqual(MapKeyStatus.Resolved, resolution.Status);
        Assert.AreEqual("Autumn Leaves v2", resolution.MapName);
    }

    [Test]
    public void TwoIdsWithOneName_AreOneMap_WithTheMostPlayedFile()
    {
        var resolver = Resolver(new[]
        {
            Event("W3Champions/Legion_TD_11.2_W3C_TeamOZE.w3x", 9001, "Legion TD", 21, Legion4v4, 65),
            Event("W3Champions/Legion_TD_11.2_W3C_TeamOZE_.w3x", 9002, "Legion TD", 21, Legion4v4, 400)
        });

        var resolution = resolver.Resolve(MapKeyScope.SeasonAndMode, "ionTD11_2W3CTeamOZE", 21, Legion4v4);

        Assert.AreEqual(MapKeyStatus.Resolved, resolution.Status);
        Assert.AreEqual("Legion_TD_11.2_W3C_TeamOZE_.w3x", resolution.Map);
        Assert.AreEqual(9002, resolution.MapId);
        Assert.AreEqual(2, resolution.CandidateFiles.Count);
    }

    [Test]
    public void DifferentMapsSharingAKey_AreAmbiguousMerged_ToTheMostPlayed()
    {
        var resolver = Resolver(new[]
        {
            Event("W3Champions/w3c_twilightruins_lv.w3x", 35, "Twilight Ruins LV", 11, FourVsFour, 500),
            Event("W3Champions/w3c_twilightruins_anon.w3x", 19, "Twilight Ruins", 11, FourVsFour, 20)
        });

        var resolution = new MapKeyResolver(resolverEvents(), null, null, ManualMap.Empty).Resolve(MapKeyScope.SeasonAndMode, "twilightruins", 11, FourVsFour);

        Assert.AreEqual(MapKeyStatus.AmbiguousMerged, resolution.Status);
        Assert.AreEqual("w3c_twilightruins_lv.w3x", resolution.Map);
        Assert.AreEqual(35, resolution.MapId);

        // A manual entry decides instead.
        Assert.AreEqual(MapKeyStatus.ResolvedManual, resolver.Resolve(MapKeyScope.SeasonAndMode, "twilightruins", 11, FourVsFour).Status);

        IEnumerable<EventMapGroup> resolverEvents() => new[]
        {
            Event("W3Champions/w3c_twilightruins_lv.w3x", 35, "Twilight Ruins LV", 11, FourVsFour, 500),
            Event("W3Champions/w3c_twilightruins_anon.w3x", 19, "Twilight Ruins", 11, FourVsFour, 20)
        };
    }

    [Test]
    public void LegionKeySharedByTwoModes_IsSplitByMode_AndAmbiguousWithoutMode()
    {
        var resolver = Resolver(new[]
        {
            Event("W3Champions/Legion_TD_11.4b__pracmi_W3C_Team_OZE.w3x", 5459, "Legion TD: PRACMI 4v4", 25, Legion4v4, 578),
            Event("W3Champions/Legion_TD_11.4b_pracmi_W3C_Team_OZE.w3x", 5546, "Legion TD: PRACMI", 25, Legion1v1, 4647)
        });

        Assert.AreEqual(5459, resolver.Resolve(MapKeyScope.SeasonAndMode, "ionTD11_4bpracmiW3CTeamOZE", 25, Legion4v4).MapId);
        Assert.AreEqual(5546, resolver.Resolve(MapKeyScope.SeasonAndMode, "ionTD11_4bpracmiW3CTeamOZE", 25, Legion1v1).MapId);

        var withoutMode = resolver.Resolve(MapKeyScope.SeasonOnly, "ionTD11_4bpracmiW3CTeamOZE", 25, null);
        Assert.AreEqual(MapKeyStatus.AmbiguousMerged, withoutMode.Status);
        Assert.AreEqual(5546, withoutMode.MapId);
    }

    [Test]
    public void AllSeasons_UsesOneVsOneEventsOfEverySeason_AndTheManualMap()
    {
        var resolver = Resolver(new[]
        {
            Event("W3Champions/v10/w3c_AutumnLeaves_v2-0.w3x", 44, "Autumn Leaves", 11, OneVsOne, 35803),
            Event("W3Champions/v11/w3c_Ferocity_v1.2.w3x", 51, "Ferocity", 11, FourVsFour, 695),
            Event("W3Champions/1051_w3c_260615_2308_EchoIsles_v2.2.w3x", 1051, "Echo Isles v2", 25, OneVsOne, 27148)
        });

        var echo = resolver.Resolve(MapKeyScope.AllSeasonsOneVsOne, "1w3c2606152308EchoIslesv2_2", null, OneVsOne);
        var autumn = resolver.Resolve(MapKeyScope.AllSeasonsOneVsOne, "AutumnLeavesv2-0", null, OneVsOne);
        var oldAutumn = resolver.Resolve(MapKeyScope.AllSeasonsOneVsOne, "autumnleaves", null, OneVsOne);
        var ferocity = resolver.Resolve(MapKeyScope.AllSeasonsOneVsOne, "Ferocityv1_2", null, OneVsOne);

        Assert.AreEqual(MapKeyStatus.Resolved, echo.Status);
        Assert.AreEqual(1051, echo.MapId);
        Assert.AreEqual("Autumn Leaves v2", autumn.MapName);
        Assert.AreEqual("w3c_AutumnLeaves_v2-0.w3x", autumn.Map);
        Assert.AreEqual(MapKeyStatus.LegacyKey, oldAutumn.Status);
        Assert.AreEqual("Autumn Leaves", oldAutumn.MapName);
        Assert.AreEqual(MapKeyStatus.Unresolved, ferocity.Status);
    }
}
