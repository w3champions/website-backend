using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.W3ChampionsStats.MapsPerSeasons;
using W3ChampionsStatisticService.W3ChampionsStats.OverallRaceAndWinStats;

namespace WC3ChampionsStatisticService.Tests.Statistics;

// Pure tests for the map-keys write path: stats are keyed on the map file name and carry
// MapId/MapName from the event. Deliberately NOT IntegrationTestBase (shared remote Mongo).
[TestFixture]
public class MapKeyWritePathTests
{
    private const string EchoIslesFile = "1051_w3c_260615_2308_EchoIsles_v2.2.w3x";
    private const string EchoIslesOtherFile = "1051_w3c_260915_0738_EchoIsles_v2.2.w3x";
    private const string LegacyEchoIslesKey = "1w3c2606152308EchoIslesv2_2";

    [Test]
    [TestCase("W3Champions/" + EchoIslesFile, EchoIslesFile)]
    [TestCase(@"W3Champions\Temporary\" + EchoIslesFile, EchoIslesFile)]
    [TestCase("Maps/frozenthrone/(2)terenasstand_lv.w3x", "(2)terenasstand_lv.w3x")]
    [TestCase(EchoIslesFile, EchoIslesFile)]
    [TestCase(null, null)]
    public void MapName_IsTheFileName(string path, string expected)
    {
        Assert.That(new MapName(path).Name, Is.EqualTo(expected));
    }

    [Test]
    public void MapsPerSeason_Count_StoresIdAndNameInOverallAndModeBuckets()
    {
        var stats = MapsPerSeason.Create(25);

        stats.Count(EchoIslesFile, 1051, "Echo Isles v2", GameMode.GM_1v1);

        foreach (var gameMode in new[] { GameMode.Undefined, GameMode.GM_1v1 })
        {
            var entry = stats.MatchesOnMapPerModes.Single(m => m.GameMode == gameMode).Maps.Single();
            Assert.That(entry.Map, Is.EqualTo(EchoIslesFile));
            Assert.That(entry.MapId, Is.EqualTo(1051));
            Assert.That(entry.MapName, Is.EqualTo("Echo Isles v2"));
            Assert.That(entry.Count, Is.EqualTo(1));
        }
    }

    [Test]
    public void MapsPerSeason_Count_RefreshesWithLatestNonNullValues()
    {
        var stats = MapsPerSeason.Create(25);

        stats.Count(EchoIslesFile, 1051, "Echo Isles v2", GameMode.GM_1v1);
        stats.Count(EchoIslesFile, null, null, GameMode.GM_1v1);
        var afterNulls = stats.MatchesOnMapPerModes.Single(m => m.GameMode == GameMode.GM_1v1).Maps.Single();
        Assert.That(afterNulls.MapId, Is.EqualTo(1051));
        Assert.That(afterNulls.MapName, Is.EqualTo("Echo Isles v2"));

        stats.Count(EchoIslesFile, 1051, "Echo Isles", GameMode.GM_1v1);
        var afterRename = stats.MatchesOnMapPerModes.Single(m => m.GameMode == GameMode.GM_1v1).Maps.Single();
        Assert.That(afterRename.MapName, Is.EqualTo("Echo Isles"));
        Assert.That(afterRename.Count, Is.EqualTo(3));
    }

    [Test]
    public void MapsPerSeason_Count_KeepsFilesOfTheSameMapIdSeparate()
    {
        var stats = MapsPerSeason.Create(25);

        stats.Count(EchoIslesFile, 1051, "Echo Isles v2", GameMode.GM_1v1);
        stats.Count(EchoIslesOtherFile, 1051, "Echo Isles v2", GameMode.GM_1v1);

        var maps = stats.MatchesOnMapPerModes.Single(m => m.GameMode == GameMode.GM_1v1).Maps;
        Assert.That(maps.Select(m => m.Map), Is.EqualTo(new[] { EchoIslesFile, EchoIslesOtherFile }));
        Assert.That(maps.Select(m => m.Count), Is.EqualTo(new[] { 1, 1 }));
    }

    [Test]
    [TestCase("1.32.5")]
    [TestCase("All")]
    public void PlayerRaceOnMap_AddMapWin_StoresIdAndNameOnRaceAndTotal(string patch)
    {
        var player = PlayerRaceOnMapVersusRaceRatio.Create("peter#123", 25);

        player.AddMapWin(Race.HU, Race.UD, EchoIslesFile, 1051, "Echo Isles v2", true, patch);

        foreach (var race in new[] { Race.HU, Race.Total })
        {
            var entry = player.RaceWinsOnMapByPatch[patch].Single(r => r.Race == race).WinLossesOnMap.Single();
            Assert.That(entry.Map, Is.EqualTo(EchoIslesFile));
            Assert.That(entry.MapId, Is.EqualTo(1051));
            Assert.That(entry.MapName, Is.EqualTo("Echo Isles v2"));
            Assert.That(entry.WinLosses.Single(w => w.Race == Race.UD).Wins, Is.EqualTo(1));
        }
    }

    [Test]
    public void PlayerRaceOnMap_AddMapWin_OverallHasNoIdOrName()
    {
        var player = PlayerRaceOnMapVersusRaceRatio.Create("peter#123", 25);

        player.AddMapWin(Race.HU, Race.UD, "Overall", null, null, true, "All");

        var entry = player.RaceWinsOnMapByPatch["All"].Single(r => r.Race == Race.HU).WinLossesOnMap.Single();
        Assert.That(entry.Map, Is.EqualTo("Overall"));
        Assert.That(entry.MapId, Is.Null);
        Assert.That(entry.MapName, Is.Null);
    }

    [Test]
    public void OverallRaceAndWinStat_Apply_KeysOnFileWithOverallFirst()
    {
        var stat = new OverallRaceAndWinStat(0);

        stat.Apply("Overall", null, "Overall", Race.HU, Race.UD, true, "1.32.5");
        stat.Apply(EchoIslesFile, 1051, "Echo Isles v2", Race.HU, Race.UD, true, "1.32.5");
        stat.Apply(EchoIslesFile, 1051, "Echo Isles v2", Race.UD, Race.HU, false, "1.32.5");

        var entries = stat.PatchToStatsPerModes["1.32.5"];
        Assert.That(entries.Select(e => e.Map), Is.EqualTo(new[] { "Overall", EchoIslesFile }));
        Assert.That(entries[0].MapName, Is.EqualTo("Overall"));
        Assert.That(entries[0].MapId, Is.Null);
        Assert.That(entries[1].MapId, Is.EqualTo(1051));
        Assert.That(entries[1].MapName, Is.EqualTo("Echo Isles v2"));
        Assert.That(WinsOf(entries[1], Race.HU, Race.UD), Is.EqualTo(1));
        Assert.That(LossesOf(entries[1], Race.UD, Race.HU), Is.EqualTo(1));
    }

    [Test]
    public void OverallRaceAndWinStat_Apply_AdoptsLegacyOverallEntry()
    {
        var stat = new OverallRaceAndWinStat(0)
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

        stat.Apply("Overall", null, "Overall", Race.HU, Race.UD, true, "All");
        stat.Apply(EchoIslesFile, 1051, "Echo Isles v2", Race.HU, Race.UD, true, "All");

        var entries = stat.PatchToStatsPerModes["All"];
        Assert.That(entries.Count, Is.EqualTo(3));
        Assert.That(entries[0].Map, Is.EqualTo("Overall"));
        Assert.That(entries[0].MapName, Is.EqualTo("Overall"));
        Assert.That(WinsOf(entries[0], Race.HU, Race.UD), Is.EqualTo(1));
        Assert.That(entries[1].Map, Is.Null);
        Assert.That(entries[1].MapName, Is.EqualTo(LegacyEchoIslesKey));
        Assert.That(WinsOf(entries[1], Race.HU, Race.UD), Is.EqualTo(0));
        Assert.That(entries[2].Map, Is.EqualTo(EchoIslesFile));
    }

    [Test]
    public async Task MapsPerSeasonHandler_CountsFileNameWithIdAndName()
    {
        var saved = new List<MapsPerSeason>();
        var repo = new Mock<IW3StatsRepo>();
        repo.Setup(r => r.LoadMapsPerSeason(It.IsAny<int>())).ReturnsAsync((MapsPerSeason)null);
        repo.Setup(r => r.Save(It.IsAny<MapsPerSeason>()))
            .Callback<MapsPerSeason>(saved.Add)
            .Returns(Task.CompletedTask);

        var fakeEvent = TestDtoHelper.CreateFakeEvent();
        fakeEvent.match.map = "W3Champions/" + EchoIslesFile;
        fakeEvent.match.mapId = 1051;
        fakeEvent.match.mapName = "Echo Isles v2";
        fakeEvent.match.season = 25;

        await new MapsPerSeasonHandler(repo.Object).Update(fakeEvent);

        Assert.That(saved.Select(s => s.Season), Is.EquivalentTo(new[] { -1, 25 }));
        foreach (var stats in saved)
        {
            var entry = stats.MatchesOnMapPerModes.Single(m => m.GameMode == GameMode.GM_1v1).Maps.Single();
            Assert.That(entry.Map, Is.EqualTo(EchoIslesFile));
            Assert.That(entry.MapId, Is.EqualTo(1051));
            Assert.That(entry.MapName, Is.EqualTo("Echo Isles v2"));
        }
    }

    private static int WinsOf(MapToRaceVsRaceRatio entry, Race myRace, Race enemyRace)
    {
        return entry.Ratio.Single(r => r.Race == myRace).WinLosses.Single(w => w.Race == enemyRace).Wins;
    }

    private static int LossesOf(MapToRaceVsRaceRatio entry, Race myRace, Race enemyRace)
    {
        return entry.Ratio.Single(r => r.Race == myRace).WinLosses.Single(w => w.Race == enemyRace).Losses;
    }
}
