using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using W3C.Domain.CommonValueObjects;
using W3ChampionsStatisticService.Matches;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.W3ChampionsStats;
using W3ChampionsStatisticService.W3ChampionsStats.DistinctPlayersPerDays;
using W3ChampionsStatisticService.W3ChampionsStats.GamesPerDays;
using W3ChampionsStatisticService.W3ChampionsStats.MapsPerSeasons;
using W3ChampionsStatisticService.W3ChampionsStats.OverallRaceAndWinStats;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.Services;

namespace WC3ChampionsStatisticService.Tests.Statistics;

// Mock MatchmakingProvider for testing
public class MockMatchmakingProvider : IMatchmakingProvider
{
    private readonly List<GameMode> _activeGameModes;

    public MockMatchmakingProvider(List<GameMode> activeGameModes = null)
    {
        _activeGameModes = activeGameModes ?? new List<GameMode>
        {
            GameMode.GM_1v1,
            GameMode.GM_2v2,
            GameMode.GM_4v4,
            GameMode.FFA
        };
    }

    public Task<List<ActiveGameMode>> GetCurrentlyActiveGameModesAsync()
    {
        var activeModes = _activeGameModes.Select(gameMode => new ActiveGameMode
        {
            Id = gameMode,
            Maps = new List<MapShortInfo>(), // Empty list for testing
            Name = gameMode.ToString(),
            Type = "test"
        }).ToList();
        return Task.FromResult(activeModes);
    }
}

[TestFixture]
public class W3Stats : IntegrationTestBase
{
    private string _jwt = "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJiYXR0bGVUYWciOiJtb2Rtb3RvIzI4MDkiLCJpc0FkbWluIjoiVHJ1ZSIsIm5hbWUiOiJtb2Rtb3RvIn0.0rJooIabRqj_Gt0fuuW5VP6ICdV1FJfwRJYuhesou7rPqE9HWZRewm12bd4iWusa4lcYK6vp5LCr6fBj4XUc2iQ4Bo9q3qtu54Rwc-eH2m-_7VqJE6D3yLm7Gcre0NE2LHZjh7qA5zHQn5kU_ugOmcovaVJN_zVEM1wRrVwR6mkNDwIwv3f_A_3AQOB8s0rin0MS4950DnFkmM0CLQ-MMzwFHg_kKgiStSiAp-2Mlu5SijGUx8keM3ArjOj7Kplk_wxjPCkjplIfAHb5qXBpdcO5exXD7UJwETqUHu4NgH-9-GWzPPNCW5BMfzPV-BMiO1sESEb4JZUZqTSJCnAG2d1mx_yukDHR_8ZSd-rB5en2WzOdN1Fjds_M0u5BvnAaLQOzz69YURL4mnI-jiNpFNokRWYjzG-_qEVJTRtUugiCipT6SMs3SlwWujxXsNSZZU0LguOuAh4EqF9ST7m_ttOcZvg5G1RLOy6A1QzWVG06Byw-7dZvMpoHrMSqjlNcJk7XtDamAVDyUNpjrqlu_I17U5DN6f8evfBtngsSgpjeswy6ccul10HRNO210I7VejGOmEsxnIDWyF-5p-UIuOaTgMiXhElwSpkIaLGQJXHFXc859UjvqC7jSRnPWpRlYRo7UpKmCJ59fgK-SzZlbp27gN_1uhk18eEWrenn6ew";

    [Test]
    public async Task LoadAndSavePersistsDateTimeInfo()
    {
        var fakeEvent = TestDtoHelper.CreateFakeEvent();

        fakeEvent.match.endTime = 1585701559200;

        var w3StatsRepo = new W3StatsRepo(MongoClient);
        var mockMatchmakingProvider = new MockMatchmakingProvider();
        var gamesPerDay = new GamesPerDayHandler(w3StatsRepo, mockMatchmakingProvider);
        await gamesPerDay.Update(fakeEvent);
        await gamesPerDay.Update(fakeEvent);

        var gamesReloaded = await w3StatsRepo.LoadGamesPerDay(new DateTime(2020, 4, 1), GameMode.Undefined, GateWay.Europe);

        Assert.AreEqual(2, gamesReloaded.GamesPlayed);
    }

    [Test]
    public async Task LoadAndSave_DifferentMode()
    {
        var fakeEvent1 = TestDtoHelper.CreateFakeEvent();
        var fakeEvent2 = TestDtoHelper.CreateFakeEvent();

        fakeEvent1.match.endTime = 1585701559200;
        fakeEvent2.match.endTime = 1585701559200;

        fakeEvent1.match.gameMode = GameMode.GM_1v1;
        fakeEvent2.match.gameMode = GameMode.GM_2v2;

        var w3StatsRepo = new W3StatsRepo(MongoClient);
        var mockMatchmakingProvider = new MockMatchmakingProvider();
        var gamesPerDayHandler = new GamesPerDayHandler(w3StatsRepo, mockMatchmakingProvider);

        await gamesPerDayHandler.Update(fakeEvent1);
        await gamesPerDayHandler.Update(fakeEvent1);
        await gamesPerDayHandler.Update(fakeEvent2);

        var gamesReloaded1 = await w3StatsRepo.LoadGamesPerDay(new DateTime(2020, 4, 1), GameMode.Undefined, GateWay.Europe);
        var gamesReloaded2 = await w3StatsRepo.LoadGamesPerDay(new DateTime(2020, 4, 1), GameMode.GM_1v1, GateWay.Europe);
        var gamesReloaded3 = await w3StatsRepo.LoadGamesPerDay(new DateTime(2020, 4, 1), GameMode.GM_2v2, GateWay.Europe);

        Assert.AreEqual(3, gamesReloaded1.GamesPlayed);
        Assert.AreEqual(GameMode.Undefined, gamesReloaded1.GameMode);
        Assert.AreEqual(2, gamesReloaded2.GamesPlayed);
        Assert.AreEqual(GameMode.GM_1v1, gamesReloaded2.GameMode);
        Assert.AreEqual(1, gamesReloaded3.GamesPlayed);
        Assert.AreEqual(GameMode.GM_2v2, gamesReloaded3.GameMode);
    }

    [Test]
    public async Task LoadAndSave_DifferentGW()
    {
        var fakeEvent1 = TestDtoHelper.CreateFakeEvent();
        var fakeEvent2 = TestDtoHelper.CreateFakeEvent();

        fakeEvent1.match.endTime = 1585701559200;
        fakeEvent2.match.endTime = 1585701559200;

        fakeEvent1.match.gameMode = GameMode.GM_1v1;
        fakeEvent1.match.gateway = GateWay.America;
        fakeEvent2.match.gateway = GateWay.Europe;
        fakeEvent2.match.gameMode = GameMode.GM_2v2;

        var w3StatsRepo = new W3StatsRepo(MongoClient);
        var mockMatchmakingProvider = new MockMatchmakingProvider();
        var gamesPerDayHandler = new GamesPerDayHandler(w3StatsRepo, mockMatchmakingProvider);

        await gamesPerDayHandler.Update(fakeEvent1);
        await gamesPerDayHandler.Update(fakeEvent1);
        await gamesPerDayHandler.Update(fakeEvent2);

        var gamesReloaded1 = await w3StatsRepo.LoadGamesPerDay(new DateTime(2020, 4, 1), GameMode.GM_1v1, GateWay.Europe);
        var gamesReloaded2 = await w3StatsRepo.LoadGamesPerDay(new DateTime(2020, 4, 1), GameMode.GM_1v1, GateWay.America);
        var gamesReloaded3 = await w3StatsRepo.LoadGamesPerDay(new DateTime(2020, 4, 1), GameMode.GM_2v2, GateWay.Europe);
        var gamesReloaded4 = await w3StatsRepo.LoadGamesPerDay(new DateTime(2020, 4, 1), GameMode.GM_2v2, GateWay.America);
        var gamesReloaded5 = await w3StatsRepo.LoadGamesPerDay(new DateTime(2020, 4, 1), GameMode.Undefined, GateWay.Undefined);

        Assert.AreEqual(0, gamesReloaded1.GamesPlayed);
        Assert.AreEqual(2, gamesReloaded2.GamesPlayed);
        Assert.AreEqual(GameMode.GM_1v1, gamesReloaded2.GameMode);
        Assert.AreEqual(GateWay.America, gamesReloaded2.GateWay);
        Assert.AreEqual(1, gamesReloaded3.GamesPlayed);
        Assert.AreEqual(GateWay.Europe, gamesReloaded3.GateWay);
        Assert.AreEqual(GameMode.GM_2v2, gamesReloaded3.GameMode);
        Assert.AreEqual(0, gamesReloaded4.GamesPlayed);
        Assert.AreEqual(3, gamesReloaded5.GamesPlayed);
    }

    [Test]
    public async Task DistincPlayerPerDay()
    {
        var time1 = new DateTime(2020, 10, 17);
        var gamesPerDay1 = DistinctPlayersPerDay.Create(new DateTimeOffset(time1));
        var time2 = new DateTime(2020, 10, 16);
        var gamesPerDay2 = DistinctPlayersPerDay.Create(new DateTimeOffset(time2));
        gamesPerDay1.AddPlayer("peter");
        gamesPerDay1.AddPlayer("wolf");
        gamesPerDay2.AddPlayer("peter");

        var w3StatsRepo = new W3StatsRepo(MongoClient);
        await w3StatsRepo.Save(gamesPerDay1);
        await w3StatsRepo.Save(gamesPerDay2);

        var gamesReloaded1 = await w3StatsRepo.LoadPlayersPerDay(time1);
        var gamesReloaded2 = await w3StatsRepo.LoadPlayersPerDay(time2);

        Assert.AreEqual(2, gamesReloaded1.DistinctPlayers);
        Assert.AreEqual(2, gamesReloaded1.DistinctPlayers);
        Assert.AreEqual(1, gamesReloaded2.DistinctPlayers);
        Assert.AreEqual("peter", gamesReloaded1.Players[0]);
        Assert.AreEqual("wolf", gamesReloaded1.Players[1]);
        Assert.AreEqual("peter", gamesReloaded2.Players[0]);
    }

    [Test]
    public async Task RaceVsRaceOnMapStatsTest_GroupByMMR()
    {
        var fakeEvent1 = TestDtoHelper.CreateFakeEvent();
        var fakeEvent2 = TestDtoHelper.CreateFakeEvent();

        fakeEvent1.match.players[0].mmr.rating = 1300;
        fakeEvent1.match.players[1].mmr.rating = 1300;
        fakeEvent1.match.startTime = 1591374182684;

        fakeEvent2.match.players[0].mmr.rating = 1800;
        fakeEvent2.match.players[1].mmr.rating = 1900;
        fakeEvent2.match.startTime = 1591370203764;

        await InsertMatchEvents(new List<MatchFinishedEvent> { fakeEvent1, fakeEvent2 });

        var w3StatsRepo = new W3StatsRepo(MongoClient);
        var patchRepo = new PatchRepository(MongoClient);

        var patch1 = new Patch
        {
            Version = "1.32.5",
            StartDate = DateTime.SpecifyKind(new DateTime(2020, 4, 27, 0, 0, 0), DateTimeKind.Utc)
        };

        var patch2 = new Patch
        {
            Version = "1.32.6",
            StartDate = DateTime.SpecifyKind(new DateTime(2020, 6, 3, 19, 0, 0), DateTimeKind.Utc)
        };

        await patchRepo.InsertPatches(new List<Patch>() { patch1, patch2 });
        var overallRaceAndWinStatsHandler = new OverallRaceAndWinStatHandler(w3StatsRepo, patchRepo);

        await overallRaceAndWinStatsHandler.Update(fakeEvent1);
        await overallRaceAndWinStatsHandler.Update(fakeEvent2);

        var result = await w3StatsRepo.LoadRaceVsRaceStats();

        Assert.AreEqual(3, result.Count);

        Assert.AreEqual(0, result[0].MmrRange);
        Assert.AreEqual(1200, result[1].MmrRange);
        Assert.AreEqual(1800, result[2].MmrRange);
    }

    [Test]
    public async Task MatchesOnMap()
    {
        var w3StatsRepo = new W3StatsRepo(MongoClient);
        var mapsPerSeasonHandler = new MapsPerSeasonHandler(w3StatsRepo);

        var fakeEvent1 = TestDtoHelper.CreateFakeEvent();
        var fakeEvent2 = TestDtoHelper.CreateFakeEvent();
        var fakeEvent3 = TestDtoHelper.CreateFakeEvent();

        fakeEvent1.match.gameMode = GameMode.GM_1v1;
        fakeEvent2.match.gameMode = GameMode.GM_1v1;
        fakeEvent3.match.gameMode = GameMode.GM_1v1;

        fakeEvent1.match.map = "(2)Map1.w3x";
        fakeEvent2.match.map = "(2)Map1.w3x";
        fakeEvent3.match.map = "(2)Map2.w3x";

        fakeEvent1.match.season = 0;
        fakeEvent2.match.season = 1;
        fakeEvent3.match.season = 1;

        await mapsPerSeasonHandler.Update(fakeEvent1);
        await mapsPerSeasonHandler.Update(fakeEvent2);
        await mapsPerSeasonHandler.Update(fakeEvent3);

        var loadMapsPerSeasonOverall = await w3StatsRepo.LoadMapsPerSeason(-1);
        var loadMapsPerSeason1 = await w3StatsRepo.LoadMapsPerSeason(0);
        var loadMapsPerSeason2 = await w3StatsRepo.LoadMapsPerSeason(1);
        var loadMapsPerSeason3 = await w3StatsRepo.LoadMapsPerSeason(2);

        Assert.AreEqual(2, loadMapsPerSeasonOverall.MatchesOnMapPerModes[0].Maps.Single(m => m.Map == "Map1").Count);
        Assert.AreEqual(1, loadMapsPerSeason1.MatchesOnMapPerModes[0].Maps.Single(m => m.Map == "Map1").Count);
        Assert.AreEqual(1, loadMapsPerSeason2.MatchesOnMapPerModes[0].Maps.Single(m => m.Map == "Map1").Count);
        Assert.AreEqual(1, loadMapsPerSeason2.MatchesOnMapPerModes[0].Maps.Single(m => m.Map == "Map2").Count);
        Assert.IsNull(loadMapsPerSeason3);
    }

    [Test]
    [TestCase("path/w3c_1v1_autumnleaves_anon", "autumnleaves")]
    [TestCase("path/w3c_1v1_terenasstand_anon", "terenasstand")]
    [TestCase("path/w3c_gnollwood_anon", "gnollwood")]
    [TestCase("path/w3c_tidewaterglades_lv_anon", "tidewaterglades")]
    [TestCase("path/w3c_tidewaterglades_anon", "tidewaterglades")]
    [TestCase("path/w3c_ffa_marketsquare_anon_cd", "marketsquare")]
    [TestCase("path/w3c_ffa_marketsquare_cd", "marketsquare")]
    [TestCase("path/w3c_1v1_lastrefuge.anon.w3x", "lastrefuge")]
    public void MapName(string input, string expected)
    {
        var mapName = new MapName(input);
        Assert.AreEqual(expected, mapName.Name);
    }

    [Test]
    public void GetToken()
    {
        var w3CAuthenticationService = new W3CAuthenticationService();
        var userByToken1 = w3CAuthenticationService.GetUserByToken(_jwt, false);

        Assert.AreEqual("modmoto#2809", userByToken1.BattleTag);
    }

    [Test]
    public async Task LoadAndSave_InactiveGameModes_AreSkipped()
    {
        var fakeEvent = TestDtoHelper.CreateFakeEvent();
        fakeEvent.match.endTime = 1585701559200;
        fakeEvent.match.gameMode = GameMode.GM_3v3; // Use a game mode that won't be in our limited active list

        var w3StatsRepo = new W3StatsRepo(MongoClient);
        // Create mock with limited active modes (only GM_1v1 and GM_2v2)
        var mockMatchmakingProvider = new MockMatchmakingProvider(new List<GameMode> { GameMode.GM_1v1, GameMode.GM_2v2 });
        var gamesPerDayHandler = new GamesPerDayHandler(w3StatsRepo, mockMatchmakingProvider);

        await gamesPerDayHandler.Update(fakeEvent);

        var date = new DateTime(2020, 4, 1);

        // Check that stats were created for the active modes (GM_1v1, GM_2v2) - these get pre-created with 0 games
        var gm1v1Stats = await w3StatsRepo.LoadGamesPerDay(date, GameMode.GM_1v1, GateWay.Europe);
        var gm2v2Stats = await w3StatsRepo.LoadGamesPerDay(date, GameMode.GM_2v2, GateWay.Europe);

        // Check that the inactive GM_3v3 mode did get stats because an actual match happened in that mode
        var gm3v3Stats = await w3StatsRepo.LoadGamesPerDay(date, GameMode.GM_3v3, GateWay.Europe);

        // Check that other inactive modes (GM_4v4, FFA) did NOT get stats because no matches happened in those modes
        var gm4v4Stats = await w3StatsRepo.LoadGamesPerDay(date, GameMode.GM_4v4, GateWay.Europe);
        var ffaStats = await w3StatsRepo.LoadGamesPerDay(date, GameMode.FFA, GateWay.Europe);

        // Active modes should have stats created (even with 0 games)
        Assert.IsNotNull(gm1v1Stats, "GM_1v1 should have stats created as it's active");
        Assert.IsNotNull(gm2v2Stats, "GM_2v2 should have stats created as it's active");
        Assert.AreEqual(0, gm1v1Stats.GamesPlayed, "GM_1v1 should have 0 games played (pre-created)");
        Assert.AreEqual(0, gm2v2Stats.GamesPlayed, "GM_2v2 should have 0 games played (pre-created)");

        // The actual match mode (GM_3v3) should have stats because a match happened, even though it's inactive
        Assert.IsNotNull(gm3v3Stats, "GM_3v3 should have stats created because a match occurred");
        Assert.AreEqual(1, gm3v3Stats.GamesPlayed, "GM_3v3 should have 1 game played from the actual match");

        // Other inactive modes should not have stats created since no matches occurred
        Assert.IsNull(gm4v4Stats, "GM_4v4 should not have stats created as it's inactive and no match occurred");
        Assert.IsNull(ffaStats, "FFA should not have stats created as it's inactive and no match occurred");

        // The overall stats should still be created regardless of game mode activity
        var overallStats = await w3StatsRepo.LoadGamesPerDay(date, GameMode.Undefined, GateWay.Europe);
        Assert.IsNotNull(overallStats, "Overall stats should be created regardless of game mode activity");
        Assert.AreEqual(1, overallStats.GamesPlayed, "Overall stats should count the GM_3v3 game");
    }
}
