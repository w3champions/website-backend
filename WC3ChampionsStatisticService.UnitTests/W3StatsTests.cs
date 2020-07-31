using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.W3ChampionsStats;
using W3ChampionsStatisticService.W3ChampionsStats.DistinctPlayersPerDays;
using W3ChampionsStatisticService.W3ChampionsStats.GameLengths;
using W3ChampionsStatisticService.W3ChampionsStats.GamesPerDays;
using W3ChampionsStatisticService.W3ChampionsStats.OverallRaceAndWinStats;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class W3Stats : IntegrationTestBase
    {
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

            Assert.IsNull(gamesReloaded1);
            Assert.AreEqual(2, gamesReloaded2.GamesPlayed);
            Assert.AreEqual(GameMode.GM_1v1, gamesReloaded2.GameMode);
            Assert.AreEqual(GateWay.America, gamesReloaded2.GateWay);
            Assert.AreEqual(1, gamesReloaded3.GamesPlayed);
            Assert.AreEqual(GateWay.Europe, gamesReloaded3.GateWay);
            Assert.AreEqual(GameMode.GM_2v2, gamesReloaded3.GameMode);
            Assert.IsNull(gamesReloaded4);
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
    }
}