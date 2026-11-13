using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MongoDB.Bson;
using NUnit.Framework;
using W3ChampionsStatisticService.Tools.MapKeyBackfill;

namespace WC3ChampionsStatisticService.Tests.Tools;

public class MapKeyReportTests
{
    private const int FourVsFour = 4;
    private const int Legion4v4 = 202;

    private static readonly Dictionary<int, string> CurrentNames = new()
    {
        [77] = "Bandit's Retreat",
        [44] = "Autumn Leaves v2",
        [5459] = "Legion TD: PRACMI 4v4 renamed",
    };

    private static string CurrentName(int id) => CurrentNames.TryGetValue(id, out var name) ? name : null;

    private static MapKeyReportRow Record(MapKeyReport report, string collection, string key, int? season, int? gameMode, string file, int? mapId, string mapName)
    {
        var resolution = new MapKeyResolution(MapKeyStatus.Resolved, file ?? key, mapId, mapName, Array.Empty<string>(), "events", "");
        return report.Record(collection, new BsonString("doc"), key, season, gameMode, resolution);
    }

    [Test]
    public void DisplayName_CurrentNameMode_ShowsTheCurrentName()
    {
        var report = new MapKeyReport();
        var row = Record(report, MapKeyTransforms.MapsPerSeason, "BanditsRetreatv1_1", 14, FourVsFour, "w3c_BanditsRetreat_v1.1.w3x", 77, "Bandits Retreat");

        Assert.AreEqual("Bandit's Retreat", report.DisplayNameOf(row, CurrentName));
    }

    [Test]
    public void DisplayName_OtherMode_KeepsTheStoredName()
    {
        var report = new MapKeyReport();
        var row = Record(report, MapKeyTransforms.MapsPerSeason, "ionTD11_4bpracmiW3CTeamOZE", 25, Legion4v4, "Legion_TD_11.4b__pracmi_W3C_Team_OZE.w3x", 5459, "Legion TD: PRACMI 4v4");

        Assert.AreEqual("Legion TD: PRACMI 4v4", report.DisplayNameOf(row, CurrentName));
    }

    [Test]
    public void DisplayName_EntryWithoutMode_ShowsTheCurrentName()
    {
        var report = new MapKeyReport();
        var row = Record(report, MapKeyTransforms.PlayerHeroStats, "AutumnLeavesv2_0", 12, null, "w3c_AutumnLeaves_v2.0.w3x", 44, "Autumn Leaves");

        Assert.AreEqual("Autumn Leaves v2", report.DisplayNameOf(row, CurrentName));
    }

    [Test]
    public void DisplayName_UnknownIdOrNoId_KeepsTheStoredName()
    {
        var report = new MapKeyReport();
        var unknownId = Record(report, MapKeyTransforms.MapsPerSeason, "somemap", 14, FourVsFour, null, 999, "Some Map");
        var noId = Record(report, MapKeyTransforms.MapsPerSeason, "autumnleaves", 5, FourVsFour, null, null, "Autumn Leaves");

        Assert.AreEqual("Some Map", report.DisplayNameOf(unknownId, CurrentName));
        Assert.AreEqual("Autumn Leaves", report.DisplayNameOf(noId, CurrentName));
    }

    [Test]
    public void DisplayName_NoName_ShowsTheStoredMap()
    {
        var report = new MapKeyReport();
        var legacy = Record(report, MapKeyTransforms.PlayerRaceOnMapVersusRaceRatio, "ionTD9_0cTeamOZEW3C", 7, null, null, null, null);
        var file = Record(report, MapKeyTransforms.MapsPerSeason, "somekey", 14, Legion4v4, "some_file.w3x", null, null);

        Assert.AreEqual("ionTD9_0cTeamOZEW3C", report.DisplayNameOf(legacy, CurrentName));
        Assert.AreEqual("some_file.w3x", report.DisplayNameOf(file, CurrentName));
    }

    [Test]
    public void DisplayName_AllModesBucket_FollowsTheModesTheIdIsPlayedIn()
    {
        var report = new MapKeyReport();
        Record(report, MapKeyTransforms.MapsPerSeason, "BanditsRetreatv1_1", 14, FourVsFour, "w3c_BanditsRetreat_v1.1.w3x", 77, "Bandits Retreat");
        Record(report, MapKeyTransforms.MapsPerSeason, "ionTD11_4bpracmiW3CTeamOZE", 14, Legion4v4, "Legion_TD_11.4b__pracmi_W3C_Team_OZE.w3x", 5459, "Legion TD: PRACMI 4v4");
        var melee = Record(report, MapKeyTransforms.MapsPerSeason, "BanditsRetreatv1_1", 14, GameModeNames.Undefined, "w3c_BanditsRetreat_v1.1.w3x", 77, "Bandits Retreat");
        var legion = Record(report, MapKeyTransforms.MapsPerSeason, "ionTD11_4bpracmiW3CTeamOZE", 14, GameModeNames.Undefined, "Legion_TD_11.4b__pracmi_W3C_Team_OZE.w3x", 5459, "Legion TD: PRACMI 4v4");
        var otherSeason = Record(report, MapKeyTransforms.MapsPerSeason, "BanditsRetreatv1_1", 15, GameModeNames.Undefined, "w3c_BanditsRetreat_v1.1.w3x", 77, "Bandits Retreat");

        Assert.AreEqual("Bandit's Retreat", report.DisplayNameOf(melee, CurrentName));
        Assert.AreEqual("Legion TD: PRACMI 4v4", report.DisplayNameOf(legion, CurrentName));
        Assert.AreEqual("Bandits Retreat", report.DisplayNameOf(otherSeason, CurrentName));
    }

    [Test]
    public void Write_AddsTheDisplayNameAfterTheStoredName()
    {
        var report = new MapKeyReport();
        Record(report, MapKeyTransforms.MapsPerSeason, "BanditsRetreatv1_1", 14, FourVsFour, "w3c_BanditsRetreat_v1.1.w3x", 77, "Bandits Retreat");
        var path = Path.Combine(Path.GetTempPath(), $"map-key-report-{Guid.NewGuid():N}.csv");

        try
        {
            report.Write(path, CurrentName);
            var lines = File.ReadAllLines(path);
            var header = lines[0].Split(',').ToList();
            var fields = lines[1].Split(',');

            Assert.AreEqual(header.IndexOf("map_name") + 1, header.IndexOf("display_name"));
            Assert.AreEqual("Bandits Retreat", fields[header.IndexOf("map_name")]);
            Assert.AreEqual("Bandit's Retreat", fields[header.IndexOf("display_name")]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
