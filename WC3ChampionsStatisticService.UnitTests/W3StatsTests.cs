using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.W3ChampionsStats;
using W3ChampionsStatisticService.W3ChampionsStats.DistinctPlayersPerDays;
using W3ChampionsStatisticService.W3ChampionsStats.GameLengths;
using W3ChampionsStatisticService.W3ChampionsStats.GamesPerDays;
using W3ChampionsStatisticService.W3ChampionsStats.MapsPerSeasons;
using W3ChampionsStatisticService.W3ChampionsStats.OverallRaceAndWinStats;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class W3Stats : IntegrationTestBase
    {
        private string _jwt = "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJCYXR0bGVUYWciOiJtb2Rtb3RvIzI4MDkiLCJJc0FkbWluIjoiVHJ1ZSIsIk5hbWUiOiJtb2Rtb3RvIn0.Y4xe1wqRceSdJW2evar5LFVsWfixZUUQtWWckehnkNwVpGiNIzQb90GP30fzOFt9GKUXO7ADNuy4ss8tTNxlvSiYmkT9Ulx1-ve64WO8SYJUBwFVqPorBrunz628tFyf4t1YMt_q_lfbVuQc1WdJiNVqFy1FNzkWENW-GsZbJB-shrCIVj9qp_MtP7MC0Bata7XCjTszlZnVAJUh7-iBPlUhSg8405U5aHkGpPzjLRgQtlGm6s8F1lYOyIzT-rCCvAI_dVI3F4ee6cjS0MbY9m8KPjloOx2NJGKvbwE0dAKBszKbQ7Ic3zr6yCvj-FBt82VmAaDan7pzXJLyZcSnFbikhsKSjLzcAXw1fP_I-FhEIvS-9vysWmXx9uNF91cDlXvdZZo57gV7o6vS4CgXscvpwiPQ9KnKsQA3Ezn61snZoXjGKspiTI_yblC4zLPHm-s40RmPOI_9TwxaiOurl6GjZk1uNY5dm7cGQjh4QWbha8CkllAmgknKOfQw9Mj7TvEKukkFetKF96jOjnqBFQUVXM8YL8K9rzATEy45vkPbfTs7MP9dHUVyEUYfD-HoYMpexEkPRwpCsLty2VfDmIV9Jkj3yOh3ybeKgv7N3Dh8ROx2lxSnqZhyc5HfE_AsnjaLTq2SvEqJ4ndYtYH9rVIARx0p_gPBZF9kAl-Nb2M";

        [Test]
        public async Task LoadAndSavePersistsDateTimeInfo()
        {
            var fakeEvent = TestDtoHelper.CreateFakeEvent();

            fakeEvent.match.endTime = 1585701559200;

            var w3StatsRepo = new W3StatsRepo(MongoClient);
            var gamesPerDay = new GamesPerDayHandler(w3StatsRepo);
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
            var gamesPerDayHandler = new GamesPerDayHandler(w3StatsRepo);

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
            var gamesPerDayHandler = new GamesPerDayHandler(w3StatsRepo);

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
        public void GameLengtStatsBelow30s()
        {
            var gameLengthStats = GameLengthStat.Create();
            gameLengthStats.Apply(GameMode.GM_1v1, new TimeSpan(0, 0, 20));

            Assert.AreEqual(1, gameLengthStats.GameLengths[0].Lengths[0].Games);
            Assert.AreEqual(0, gameLengthStats.GameLengths[0].Lengths[1].Games);
        }

        [Test]
        public void GameLengtStatsLongetThan1hour()
        {
            var gameLengthStats = GameLengthStat.Create();
            gameLengthStats.Apply(GameMode.GM_1v1, new TimeSpan(1, 5, 20));

            Assert.AreEqual(1, gameLengthStats.GameLengths[0].Lengths[120].Games);
            Assert.AreEqual(3600, gameLengthStats.GameLengths[0].Lengths[120].passedTimeInSeconds);
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
            var userByToken1 = w3CAuthenticationService.GetUserByToken(_jwt);

            Assert.AreEqual("modmoto#2809", userByToken1.BattleTag);
        }
    }
}