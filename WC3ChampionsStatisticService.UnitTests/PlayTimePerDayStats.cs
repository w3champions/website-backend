using System;
using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.W3ChampionsStats;
using W3ChampionsStatisticService.W3ChampionsStats.HourOfPlay;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class PlayTimePerDayStats : IntegrationTestBase
    {

        [Test]
        public void PlayTimesPerDay()
        {
            var dateTime = new DateTimeOffset(new DateTime(2020, 10, 16));
            var hourOfPlayStats = HourOfPlayStats.Create(dateTime);

            hourOfPlayStats.Apply(GameMode.GM_1v1, dateTime, dateTime);

            Assert.AreEqual(1, hourOfPlayStats.PlayTimesPerModeTwoWeeks[0].PlayTimePerHour[0].Games);
            Assert.AreEqual(0, hourOfPlayStats.PlayTimesPerModeTwoWeeks[0].PlayTimePerHour[1].Games);
        }

        [Test]
        public void PlayTimesPerDayOneHourOff()
        {
            var dateTime = new DateTimeOffset(new DateTime(2020, 10, 16));
            var hourOfPlayStats = HourOfPlayStats.Create(dateTime);

            hourOfPlayStats.Apply(GameMode.GM_1v1, dateTime.AddHours(-1), dateTime);

            Assert.AreEqual(1, hourOfPlayStats.PlayTimesPerModeTwoWeeks[4].PlayTimePerHour[92].Games);
        }

        [Test]
        public void PlayTimesPerDayOneDayAfterInterval()
        {
            var dateTime = new DateTimeOffset(new DateTime(2020, 10, 16));
            var hourOfPlayStats = HourOfPlayStats.Create(dateTime);

            hourOfPlayStats.Apply(GameMode.GM_1v1, dateTime.AddDays(1), dateTime.AddDays(1));

            Assert.AreEqual(1, hourOfPlayStats.PlayTimesPerModeTwoWeeks[0].PlayTimePerHour[0].Games);
        }

        [Test]
        public void PlayTimesPerDay_Average()
        {
            var dateTime = new DateTimeOffset(new DateTime(2020, 10, 16));
            var hourOfPlayStats = HourOfPlayStats.Create(dateTime);

            hourOfPlayStats.Apply(GameMode.GM_1v1, dateTime, dateTime);
            hourOfPlayStats.Apply(GameMode.GM_1v1, dateTime.AddDays(-1), dateTime);
            hourOfPlayStats.Apply(GameMode.GM_1v1,  dateTime.AddDays(-2), dateTime);

            Assert.AreEqual(3, hourOfPlayStats.PlayTimesPerMode[0].PlayTimePerHour[0].Games);
            Assert.AreEqual(15, hourOfPlayStats.PlayTimesPerMode[0].PlayTimePerHour[1].Minutes);
            Assert.AreEqual(1, hourOfPlayStats.PlayTimesPerMode[0].PlayTimePerHour[4].Hours);
        }

        [Test]
        public void PlayTimesPerDay_TooOldGame()
        {
            var dateTime = new DateTimeOffset(new DateTime(2020, 10, 16));
            var hourOfPlayStats = HourOfPlayStats.Create(dateTime);

            hourOfPlayStats.Apply(GameMode.GM_1v1, dateTime.AddDays(-15), dateTime);

            Assert.AreEqual(0, hourOfPlayStats.PlayTimesPerMode[0].PlayTimePerHour[0].Games);
        }

        [Test]
        public void PlayTimesPerDay_TooOldGame_On14Days()
        {
            var dateTime = new DateTimeOffset(new DateTime(2020, 10, 16));
            var hourOfPlayStats = HourOfPlayStats.Create(dateTime);

            hourOfPlayStats.Apply(GameMode.GM_1v1, dateTime.AddDays(-14), dateTime);

            Assert.AreEqual(0, hourOfPlayStats.PlayTimesPerMode[0].PlayTimePerHour[0].Games);
        }


        [Test]
        public async Task PlayTimesPerDay_Average_TimeIsSetCorrectly_afterLoad()
        {
            var dateTime = new DateTimeOffset(new DateTime(2020, 10, 16));
            var hourOfPlayStats = HourOfPlayStats.Create(dateTime);

            var w3StatsRepo = new W3StatsRepo(MongoClient);
            await w3StatsRepo.Save(hourOfPlayStats);
            var hourOfPlayStatsLoaded = await w3StatsRepo.LoadHourOfPlay();

            Assert.AreEqual(0, hourOfPlayStatsLoaded.PlayTimesPerMode[0].PlayTimePerHour[0].Minutes);
            Assert.AreEqual(0, hourOfPlayStatsLoaded.PlayTimesPerMode[0].PlayTimePerHour[0].Hours);

            Assert.AreEqual(15, hourOfPlayStatsLoaded.PlayTimesPerMode[0].PlayTimePerHour[1].Minutes);
            Assert.AreEqual(0, hourOfPlayStatsLoaded.PlayTimesPerMode[0].PlayTimePerHour[1].Hours);

            Assert.AreEqual(0, hourOfPlayStatsLoaded.PlayTimesPerMode[0].PlayTimePerHour[4].Minutes);
            Assert.AreEqual(1, hourOfPlayStatsLoaded.PlayTimesPerMode[0].PlayTimePerHour[4].Hours);
        }
    }
}