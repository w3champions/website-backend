using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.Maps;
using W3ChampionsStatisticService.PlayerStats.HeroStats;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.Services;
using W3ChampionsStatisticService.W3ChampionsStats.MapsPerSeasons;

namespace WC3ChampionsStatisticService.Tests.Statistics;

// Pure tests for the map-keys read path: stored entries keep the name from match time, and entries
// with a map id matchmaking knows display its current name. Deliberately NOT IntegrationTestBase
// (shared remote Mongo).
[TestFixture]
public class MapKeyReadPathTests
{
    // A file no longer in the active map pool, stored under the name it had then.
    private const string EchoIslesFile = "w3c_EchoIsles_v2.2.w3x";
    private const string LegionTdFile = "Legion_TD_11.0_W3C_TeamOZE.w3x";

    private static readonly CurrentMapNames CurrentNames = new(new Dictionary<int, string>
    {
        [1051] = "Echo Isles v2",
        [5072] = "Legion TD: X3",
    });

    private static PlayerStatisticsService CreateService(
        IPlayerStatsRepository playerStatsRepository = null,
        IW3StatsRepo w3StatsRepo = null,
        CurrentMapNames currentMapNames = null)
    {
        return new PlayerStatisticsService(
            playerStatsRepository ?? Mock.Of<IPlayerStatsRepository>(),
            w3StatsRepo ?? Mock.Of<IW3StatsRepo>(),
            TestDtoHelper.CreateCurrentMapNameProvider(currentMapNames ?? CurrentMapNames.Empty).Object);
    }

    [Test]
    public void PlayerRaceOnMapVersusRaceRatioView_Create_KeepsStoredMapName()
    {
        var stat = PlayerRaceOnMapVersusRaceRatio.Create("Player#123", 25);
        stat.AddMapWin(Race.HU, Race.UD, EchoIslesFile, 1051, "Echo Isles 2.0", true, "2.0.3");
        stat.RaceWinsOnMap.AddWin(Race.HU, Race.UD, EchoIslesFile, 1051, "Echo Isles 2.0", true);

        var view = PlayerRaceOnMapVersusRaceRatioView.Create(stat);

        var byPatch = view.RaceWinsOnMapByPatch["2.0.3"].SelectMany(r => r.WinLossesOnMap).Where(m => m.Map == EchoIslesFile).ToList();
        var bySeason = view.RaceWinsOnMap.SelectMany(r => r.WinLossesOnMap).Where(m => m.Map == EchoIslesFile).ToList();
        Assert.That(byPatch, Is.Not.Empty);
        Assert.That(bySeason, Is.Not.Empty);
        Assert.That(byPatch.Concat(bySeason).All(m => m.MapName == "Echo Isles 2.0" && m.MapId == 1051), Is.True);
    }

    [Test]
    public async Task GetMapAndRaceStatAsync_ShowsCurrentName_InPatchAndLegacyEntries()
    {
        var stat = PlayerRaceOnMapVersusRaceRatio.Create("Player#123", 25);
        stat.AddMapWin(Race.HU, Race.UD, EchoIslesFile, 1051, "Echo Isles 2.0", true, "2.0.3");
        stat.RaceWinsOnMap.AddWin(Race.HU, Race.UD, EchoIslesFile, 1051, "Echo Isles 2.0", true);
        var playerStatsRepo = new Mock<IPlayerStatsRepository>();
        playerStatsRepo.Setup(r => r.LoadMapAndRaceStat("Player#123", 25)).ReturnsAsync(stat);
        var service = CreateService(playerStatsRepo.Object, currentMapNames: CurrentNames);

        var view = await service.GetMapAndRaceStatAsync("Player#123", 25);

        var entries = view.RaceWinsOnMapByPatch["2.0.3"].Concat(view.RaceWinsOnMap)
            .SelectMany(r => r.WinLossesOnMap).Where(m => m.Map == EchoIslesFile).ToList();
        Assert.That(entries, Is.Not.Empty);
        Assert.That(entries.All(m => m.MapName == "Echo Isles v2" && m.MapId == 1051), Is.True);
    }

    [Test]
    public async Task GetMapAndRaceStatAsync_WithoutCurrentNames_ReturnsStoredMapName()
    {
        var stat = PlayerRaceOnMapVersusRaceRatio.Create("Player#123", 25);
        stat.AddMapWin(Race.HU, Race.UD, EchoIslesFile, 1051, "Echo Isles 2.0", true, "2.0.3");
        var playerStatsRepo = new Mock<IPlayerStatsRepository>();
        playerStatsRepo.Setup(r => r.LoadMapAndRaceStat("Player#123", 25)).ReturnsAsync(stat);
        var service = CreateService(playerStatsRepo.Object);

        var view = await service.GetMapAndRaceStatAsync("Player#123", 25);

        var entry = view.RaceWinsOnMapByPatch["2.0.3"].SelectMany(r => r.WinLossesOnMap).First(m => m.Map == EchoIslesFile);
        Assert.That(entry.MapName, Is.EqualTo("Echo Isles 2.0"));
    }

    [Test]
    public async Task GetMapAndRaceStatAsync_NoStat_ReturnsEmptyView()
    {
        var service = CreateService(currentMapNames: CurrentNames);

        var view = await service.GetMapAndRaceStatAsync("Player#123", 25);

        Assert.That(view.Id, Is.EqualTo("25_Player#123"));
        Assert.That(view.RaceWinsOnMapByPatch, Is.Empty);
    }

    [Test]
    public async Task GetHeroStatAsync_ShowsCurrentName_AndKeepsOverall()
    {
        var heroStats = PlayerHeroStats.Create("Player#123", 25);
        heroStats.HeroStatsItemList.AddWin("archmage", Race.HU, Race.UD, EchoIslesFile, 1051, "Echo Isles 2.0", true);
        heroStats.HeroStatsItemList.AddWin("archmage", Race.HU, Race.UD, "Overall", null, null, true);
        var playerStatsRepo = new Mock<IPlayerStatsRepository>();
        playerStatsRepo.Setup(r => r.LoadHeroStat("Player#123", 25)).ReturnsAsync(heroStats);
        var service = CreateService(playerStatsRepo.Object, currentMapNames: CurrentNames);

        var loaded = await service.GetHeroStatAsync("Player#123", 25);

        var entries = loaded.HeroStatsItemList.SelectMany(h => h.Stats).SelectMany(r => r.WinLossesOnMap).ToList();
        Assert.That(entries.Where(m => m.Map == EchoIslesFile).Select(m => m.MapName), Is.All.EqualTo("Echo Isles v2"));
        Assert.That(entries.Where(m => m.Map == "Overall").Select(m => m.MapName), Is.All.Null);
    }

    [Test]
    public async Task GetHeroStatAsync_NoStat_ReturnsEmptyStats()
    {
        var service = CreateService(currentMapNames: CurrentNames);

        var loaded = await service.GetHeroStatAsync("Player#123", 25);

        Assert.That(loaded.Id, Is.EqualTo("25_Player#123"));
        Assert.That(loaded.HeroStatsItemList, Is.Empty);
    }

    [Test]
    public async Task LoadMatchesOnMapAsync_WithoutCurrentNames_ReturnsStoredIdAndName()
    {
        var stats = MapsPerSeason.Create(25);
        stats.Count(EchoIslesFile, 1051, "Echo Isles 2.0", GameMode.GM_1v1);
        var w3StatsRepo = new Mock<IW3StatsRepo>();
        w3StatsRepo.Setup(r => r.LoadMatchesOnMap()).ReturnsAsync(new List<MapsPerSeason> { stats });
        var service = CreateService(w3StatsRepo: w3StatsRepo.Object);

        var loaded = await service.LoadMatchesOnMapAsync();

        var entries = loaded.Single().MatchesOnMapPerModes.SelectMany(m => m.Maps).ToList();
        Assert.That(entries, Is.Not.Empty);
        Assert.That(entries.All(e => e.Map == EchoIslesFile && e.MapId == 1051 && e.MapName == "Echo Isles 2.0"), Is.True);
    }

    [Test]
    public async Task LoadMatchesOnMapAsync_MeleeShowsCurrentName_OtherModesKeepStoredName()
    {
        var stats = MapsPerSeason.Create(25);
        stats.Count(EchoIslesFile, 1051, "Echo Isles 2.0", GameMode.GM_1v1);
        stats.Count(LegionTdFile, 5072, "Legion TD 11.0", GameMode.GM_LEGION_1v1_x20);
        var w3StatsRepo = new Mock<IW3StatsRepo>();
        w3StatsRepo.Setup(r => r.LoadMatchesOnMap()).ReturnsAsync(new List<MapsPerSeason> { stats });
        var service = CreateService(w3StatsRepo: w3StatsRepo.Object, currentMapNames: CurrentNames);

        var loaded = await service.LoadMatchesOnMapAsync();

        var modes = loaded.Single().MatchesOnMapPerModes;
        string NameIn(GameMode mode, string map) => modes.Single(m => m.GameMode == mode).Maps.Single(e => e.Map == map).MapName;
        Assert.That(NameIn(GameMode.GM_1v1, EchoIslesFile), Is.EqualTo("Echo Isles v2"));
        Assert.That(NameIn(GameMode.GM_LEGION_1v1_x20, LegionTdFile), Is.EqualTo("Legion TD 11.0"));
        // The all-modes bucket follows the mode the map id is played in.
        Assert.That(NameIn(GameMode.Undefined, EchoIslesFile), Is.EqualTo("Echo Isles v2"));
        Assert.That(NameIn(GameMode.Undefined, LegionTdFile), Is.EqualTo("Legion TD 11.0"));
    }
}
