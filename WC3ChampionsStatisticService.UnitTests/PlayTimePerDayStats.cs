using System;
using System.Linq;
using NUnit.Framework;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.W3ChampionsStats.HourOfPlay;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class PlayTimePerDayStats
    {

        [Test]
        public void PlayTimesPerDay()
        {
            var dateTime = new DateTime(2020, 10, 16);
            var hourOfPlayStats = HourOfPlayStats.Create(dateTime);

            hourOfPlayStats.Apply(GameMode.GM_1v1, dateTime, dateTime);

            Assert.AreEqual(1, hourOfPlayStats.PlayTimesPerMode[0].PlayTimePerHour[0].Games);
            Assert.AreEqual(0, hourOfPlayStats.PlayTimesPerMode[0].PlayTimePerHour[1].Games);
        }

        [Test]
        public void PlayTimesPerDayOneHourOff()
        {
            var dateTime = new DateTime(2020, 10, 16);
            var hourOfPlayStats = HourOfPlayStats.Create(dateTime);

            hourOfPlayStats.Apply(GameMode.GM_1v1, dateTime.AddHours(-1), dateTime);

            Assert.AreEqual(1, hourOfPlayStats.PlayTimesPerMode[4].PlayTimePerHour[92].Games);
        }

        [Test]
        public void PlayTimesPerDayOneDayAfterInterval()
        {
            var dateTime = new DateTime(2020, 10, 16);
            var hourOfPlayStats = HourOfPlayStats.Create(dateTime);

            hourOfPlayStats.Apply(GameMode.GM_1v1, dateTime.AddDays(15), dateTime.AddDays(15));

            Assert.AreEqual(1, hourOfPlayStats.PlayTimesPerMode[0].PlayTimePerHour[0].Games);
        }
    }
}