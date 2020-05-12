using System;
using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.W3ChampionsStats;
using W3ChampionsStatisticService.W3ChampionsStats.DistinctPlayersPerDays;
using W3ChampionsStatisticService.W3ChampionsStats.GameLengths;
using W3ChampionsStatisticService.W3ChampionsStats.GamesPerDays;

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
            var gamesPerDay = new GamesPerDayModelHandler(w3StatsRepo);
            await gamesPerDay.Update(fakeEvent);
            await gamesPerDay.Update(fakeEvent);

            var gamesReloaded = await w3StatsRepo.LoadGamesPerDay(new DateTime(2020, 4, 1));

            Assert.AreEqual(2, gamesReloaded.GamesPlayed);
        }

        [Test]
        public void GameLengtStatsBelow30s()
        {
            var gameLengthStats = GameLengthStats.Create();
            gameLengthStats.Apply(GameMode.GM_1v1, new TimeSpan(0, 0, 20));

            Assert.AreEqual(1, gameLengthStats.GameLengths[0].Lengths[0].Games);
            Assert.AreEqual(0, gameLengthStats.GameLengths[0].Lengths[1].Games);
        }

        [Test]
        public void GameLengtStatsLongetThan1hour()
        {
            var gameLengthStats = GameLengthStats.Create();
            gameLengthStats.Apply(GameMode.GM_1v1, new TimeSpan(1, 5, 20));

            Assert.AreEqual(1, gameLengthStats.GameLengths[0].Lengths[120].Games);
            Assert.AreEqual(3600, gameLengthStats.GameLengths[0].Lengths[120].passedTimeInSeconds);
        }

        [Test]
        public async Task DistincPlayerPerDay()
        {
            var time1 = new DateTime(2020, 10, 17);
            var gamesPerDay1 = PlayersOnGameDay.Create(new DateTimeOffset(time1));
            var time2 = new DateTime(2020, 10, 16);
            var gamesPerDay2 = PlayersOnGameDay.Create(new DateTimeOffset(time2));
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
    }
}