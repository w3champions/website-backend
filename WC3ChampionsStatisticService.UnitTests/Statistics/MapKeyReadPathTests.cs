using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.Services;
using W3ChampionsStatisticService.W3ChampionsStats.MapsPerSeasons;

namespace WC3ChampionsStatisticService.Tests.Statistics;

// Pure tests for the map-keys read path: the stored MapName is returned as is, with no lookup
// against MM's currently active modes (which nulled the name of every rotated-out map).
// Deliberately NOT IntegrationTestBase (shared remote Mongo).
[TestFixture]
public class MapKeyReadPathTests
{
    // A file no longer in the active map pool.
    private const string RotatedOutFile = "1051_w3c_260615_2308_EchoIsles_v2.2.w3x";

    [Test]
    public void PlayerRaceOnMapVersusRaceRatioView_Create_KeepsStoredMapName()
    {
        var stat = PlayerRaceOnMapVersusRaceRatio.Create("Player#123", 25);
        stat.AddMapWin(Race.HU, Race.UD, RotatedOutFile, 1051, "Echo Isles v2", true, "2.0.3");
        stat.RaceWinsOnMap.AddWin(Race.HU, Race.UD, RotatedOutFile, 1051, "Echo Isles v2", true);

        var view = PlayerRaceOnMapVersusRaceRatioView.Create(stat);

        var byPatch = view.RaceWinsOnMapByPatch["2.0.3"].SelectMany(r => r.WinLossesOnMap).Where(m => m.Map == RotatedOutFile).ToList();
        var bySeason = view.RaceWinsOnMap.SelectMany(r => r.WinLossesOnMap).Where(m => m.Map == RotatedOutFile).ToList();
        Assert.That(byPatch, Is.Not.Empty);
        Assert.That(bySeason, Is.Not.Empty);
        Assert.That(byPatch.Concat(bySeason).All(m => m.MapName == "Echo Isles v2" && m.MapId == 1051), Is.True);
    }

    [Test]
    public async Task GetMapAndRaceStatAsync_ReturnsStoredMapName()
    {
        var stat = PlayerRaceOnMapVersusRaceRatio.Create("Player#123", 25);
        stat.AddMapWin(Race.HU, Race.UD, RotatedOutFile, 1051, "Echo Isles v2", true, "2.0.3");
        var playerStatsRepo = new Mock<IPlayerStatsRepository>();
        playerStatsRepo.Setup(r => r.LoadMapAndRaceStat("Player#123", 25)).ReturnsAsync(stat);
        var service = new PlayerStatisticsService(playerStatsRepo.Object, Mock.Of<IW3StatsRepo>());

        var view = await service.GetMapAndRaceStatAsync("Player#123", 25);

        var entry = view.RaceWinsOnMapByPatch["2.0.3"].SelectMany(r => r.WinLossesOnMap).First(m => m.Map == RotatedOutFile);
        Assert.That(entry.MapName, Is.EqualTo("Echo Isles v2"));
    }

    [Test]
    public async Task GetMapAndRaceStatAsync_NoStat_ReturnsEmptyView()
    {
        var service = new PlayerStatisticsService(Mock.Of<IPlayerStatsRepository>(), Mock.Of<IW3StatsRepo>());

        var view = await service.GetMapAndRaceStatAsync("Player#123", 25);

        Assert.That(view.Id, Is.EqualTo("25_Player#123"));
        Assert.That(view.RaceWinsOnMapByPatch, Is.Empty);
    }

    [Test]
    public async Task LoadMatchesOnMapAsync_ReturnsStoredIdAndName()
    {
        var stats = MapsPerSeason.Create(25);
        stats.Count(RotatedOutFile, 1051, "Echo Isles v2", GameMode.GM_1v1);
        var w3StatsRepo = new Mock<IW3StatsRepo>();
        w3StatsRepo.Setup(r => r.LoadMatchesOnMap()).ReturnsAsync(new List<MapsPerSeason> { stats });
        var service = new PlayerStatisticsService(Mock.Of<IPlayerStatsRepository>(), w3StatsRepo.Object);

        var loaded = await service.LoadMatchesOnMapAsync();

        var entries = loaded.Single().MatchesOnMapPerModes.SelectMany(m => m.Maps).ToList();
        Assert.That(entries, Is.Not.Empty);
        Assert.That(entries.All(e => e.Map == RotatedOutFile && e.MapId == 1051 && e.MapName == "Echo Isles v2"), Is.True);
    }
}
