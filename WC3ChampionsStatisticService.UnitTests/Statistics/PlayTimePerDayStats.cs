using System;
using System.Threading.Tasks;
using NUnit.Framework;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.W3ChampionsStats;
using W3ChampionsStatisticService.W3ChampionsStats.HourOfPlay;
using System.Linq;

namespace WC3ChampionsStatisticService.Tests.Statistics
{
    [TestFixture]
    public class PlayTimePerDayStats : IntegrationTestBase
    {

        [Test]
        public void PlayTimesPerDay_Midnight()
        {
            var now = DateTime.UtcNow;
            var todayMidnight = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);

            var popularHoursStats = HourOfPlayStat2.Create(GameMode.GM_1v1);

            popularHoursStats.Apply(GameMode.GM_1v1, todayMidnight);

            Assert.AreEqual(1, popularHoursStats.PlayTimesPerModeTwoWeeks.Last().PlayTimePerHour[0].Games);
            Assert.AreEqual(1, popularHoursStats.PlayTimesPerModeTotal.PlayTimePerHour[0].Games);
        }

        [Test]
        public void PlayTimesPerDay_OneHourOff()
        {
            var now = DateTime.UtcNow;
            var todayMidnight = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);

            var popularHoursStats = HourOfPlayStat2.Create(GameMode.GM_1v1);

            popularHoursStats.Apply(GameMode.GM_1v1, todayMidnight.AddHours(-1));

            Assert.AreEqual(1, popularHoursStats.PlayTimesPerModeTwoWeeks[12].PlayTimePerHour[92].Games);
            Assert.AreEqual(1, popularHoursStats.PlayTimesPerModeTotal.PlayTimePerHour[92].Games);
        }

        [Test]
        public void PlayTimesPerDay_DaysOff()
        {
            var now = DateTime.UtcNow;
            var todayMidnight = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);

            var popularHoursStats = HourOfPlayStat2.Create(GameMode.GM_1v1);

            popularHoursStats.Apply(GameMode.GM_1v1,  todayMidnight.AddDays(-1));
            popularHoursStats.Apply(GameMode.GM_1v1,  todayMidnight.AddDays(-2));

            Assert.AreEqual(1, popularHoursStats.PlayTimesPerModeTwoWeeks[12].PlayTimePerHour[0].Games);
            Assert.AreEqual(1, popularHoursStats.PlayTimesPerModeTwoWeeks[11].PlayTimePerHour[0].Games);
            Assert.AreEqual(2, popularHoursStats.PlayTimesPerModeTotal.PlayTimePerHour[0].Games);
            Assert.AreEqual(15, popularHoursStats.PlayTimesPerModeTotal.PlayTimePerHour[1].Minutes);
            Assert.AreEqual(1, popularHoursStats.PlayTimesPerModeTotal.PlayTimePerHour[4].Hours);
        }

        [Test]
        public void PlayTimesPerDay_TooOldGame()
        {
            var now = DateTime.UtcNow;
            var todayMidnight = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);

            var popularHoursStats = HourOfPlayStat2.Create(GameMode.GM_1v1);

            popularHoursStats.Apply(GameMode.GM_1v1, todayMidnight.AddDays(-15));

            int games = 0;
            foreach (var timeslot in popularHoursStats.PlayTimesPerModeTotal.PlayTimePerHour) {
                games += timeslot.Games;
            }

            Assert.AreEqual(0, games);
        }

        [Test]
        public void PlayTimesPerDay_TooOldGame_On14Days()
        {
            var now = DateTime.UtcNow;
            var todayMidnight = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);

            var popularHoursStats = HourOfPlayStat2.Create(GameMode.GM_1v1);

            popularHoursStats.Apply(GameMode.GM_1v1, todayMidnight.AddDays(-14));

            int games = 0;
            foreach (var timeslot in popularHoursStats.PlayTimesPerModeTotal.PlayTimePerHour) {
                games += timeslot.Games;
            }

            Assert.AreEqual(0, games);
        }

        [Test]
        public async Task PlayTimesPerDay_TimeslotsAreSetCorrectlyAfterLoad()
        {
            var popularHoursStats = HourOfPlayStat2.Create(GameMode.GM_1v1);

            var w3StatsRepo = new W3StatsRepo(MongoClient);
            await w3StatsRepo.Save(popularHoursStats);

            var popularHoursStatsLoaded = await w3StatsRepo.LoadHourOfPlay(GameMode.GM_1v1);

            Assert.AreEqual(0, popularHoursStatsLoaded.PlayTimesPerModeTotal.PlayTimePerHour[0].Minutes);
            Assert.AreEqual(0, popularHoursStatsLoaded.PlayTimesPerModeTotal.PlayTimePerHour[0].Hours);

            Assert.AreEqual(15, popularHoursStatsLoaded.PlayTimesPerModeTotal.PlayTimePerHour[1].Minutes);
            Assert.AreEqual(0, popularHoursStatsLoaded.PlayTimesPerModeTotal.PlayTimePerHour[1].Hours);

            Assert.AreEqual(0, popularHoursStatsLoaded.PlayTimesPerModeTotal.PlayTimePerHour[4].Minutes);
            Assert.AreEqual(1, popularHoursStatsLoaded.PlayTimesPerModeTotal.PlayTimePerHour[4].Hours);
        }
    }
}
