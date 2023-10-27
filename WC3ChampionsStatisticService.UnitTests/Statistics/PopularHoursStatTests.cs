using System;
using System.Threading.Tasks;
using NUnit.Framework;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.W3ChampionsStats;
using W3ChampionsStatisticService.W3ChampionsStats.PopularHours;
using System.Linq;

namespace WC3ChampionsStatisticService.Tests.Statistics;

[TestFixture]
public class PopularHoursStatTests : IntegrationTestBase
{

    [Test]
    public void PopularHours_Today_Midnight()
    {
        var now = DateTime.UtcNow;
        var todayMidnight = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);

        var popularHoursStats = PopularHoursStat.Create(GameMode.GM_1v1);

        popularHoursStats.Apply(GameMode.GM_1v1, todayMidnight);

        Assert.AreEqual(1, popularHoursStats.PopularHoursTwoWeeks.Last().Timeslots[0].Games);
        Assert.AreEqual(1, popularHoursStats.PopularHoursTotal.Timeslots[0].Games);
    }

    [Test]
    public void PopularHours_OneHourOff()
    {
        var now = DateTime.UtcNow;
        var todayMidnight = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);

        var popularHoursStats = PopularHoursStat.Create(GameMode.GM_1v1);

        popularHoursStats.Apply(GameMode.GM_1v1, todayMidnight.AddHours(-1));

        Assert.AreEqual(1, popularHoursStats.PopularHoursTwoWeeks[12].Timeslots[92].Games);
        Assert.AreEqual(1, popularHoursStats.PopularHoursTotal.Timeslots[92].Games);
    }

    [Test]
    public void PopularHours_DaysOff()
    {
        var now = DateTime.UtcNow;
        var todayMidnight = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);

        var popularHoursStats = PopularHoursStat.Create(GameMode.GM_1v1);

        popularHoursStats.Apply(GameMode.GM_1v1,  todayMidnight.AddDays(-1));
        popularHoursStats.Apply(GameMode.GM_1v1,  todayMidnight.AddDays(-2));

        Assert.AreEqual(1, popularHoursStats.PopularHoursTwoWeeks[12].Timeslots[0].Games);
        Assert.AreEqual(1, popularHoursStats.PopularHoursTwoWeeks[11].Timeslots[0].Games);
        Assert.AreEqual(2, popularHoursStats.PopularHoursTotal.Timeslots[0].Games);
        Assert.AreEqual(15, popularHoursStats.PopularHoursTotal.Timeslots[1].Minutes);
        Assert.AreEqual(1, popularHoursStats.PopularHoursTotal.Timeslots[4].Hours);
    }

    [Test]
    public void PopularHours_TooOldGame()
    {
        var now = DateTime.UtcNow;
        var todayMidnight = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);

        var popularHoursStats = PopularHoursStat.Create(GameMode.GM_1v1);

        popularHoursStats.Apply(GameMode.GM_1v1, todayMidnight.AddDays(-15));

        int games = 0;
        foreach (var timeslot in popularHoursStats.PopularHoursTotal.Timeslots) {
            games += timeslot.Games;
        }

        Assert.AreEqual(0, games);
    }

    [Test]
    public void PopularHours_TooOldGame_On14Days()
    {
        var now = DateTime.UtcNow;
        var todayMidnight = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);

        var popularHoursStats = PopularHoursStat.Create(GameMode.GM_1v1);

        popularHoursStats.Apply(GameMode.GM_1v1, todayMidnight.AddDays(-14));

        int games = 0;
        foreach (var timeslot in popularHoursStats.PopularHoursTotal.Timeslots) {
            games += timeslot.Games;
        }

        Assert.AreEqual(0, games);
    }

    [Test]
    public async Task PopularHours_TimeslotsAreSetCorrectlyAfterLoad()
    {
        var popularHoursStats = PopularHoursStat.Create(GameMode.GM_1v1);

        var w3StatsRepo = new W3StatsRepo(MongoClient);
        await w3StatsRepo.Save(popularHoursStats);

        var popularHoursStatsLoaded = await w3StatsRepo.LoadPopularHoursStat(GameMode.GM_1v1);

        Assert.AreEqual(0, popularHoursStatsLoaded.PopularHoursTotal.Timeslots[0].Minutes);
        Assert.AreEqual(0, popularHoursStatsLoaded.PopularHoursTotal.Timeslots[0].Hours);

        Assert.AreEqual(15, popularHoursStatsLoaded.PopularHoursTotal.Timeslots[1].Minutes);
        Assert.AreEqual(0, popularHoursStatsLoaded.PopularHoursTotal.Timeslots[1].Hours);

        Assert.AreEqual(0, popularHoursStatsLoaded.PopularHoursTotal.Timeslots[4].Minutes);
        Assert.AreEqual(1, popularHoursStatsLoaded.PopularHoursTotal.Timeslots[4].Hours);
    }
}
