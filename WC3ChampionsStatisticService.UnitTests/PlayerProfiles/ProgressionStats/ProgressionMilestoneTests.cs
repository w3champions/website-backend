using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3C.Domain.CommonValueObjects;
using W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

namespace WC3ChampionsStatisticService.UnitTests.PlayerProfiles.ProgressionStats;

[TestFixture]
public class ProgressionMilestoneTests
{
    private static List<PlayerId> Tags(params string[] tags) => tags.Select(PlayerId.Create).ToList();

    [Test]
    public void BuildId_Solo_NoRace_SortsTagsAndEmbedsGatewayInt()
    {
        var id = ProgressionMilestone.BuildId(Tags("zed#1"), GateWay.Europe, GameMode.GM_2v2_AT, null);
        Assert.AreEqual("zed#1@20_GM_2v2_AT", id);
    }

    [Test]
    public void BuildId_WithRace_AppendsRaceSuffix()
    {
        var id = ProgressionMilestone.BuildId(Tags("zed#1"), GateWay.Europe, GameMode.GM_1v1, Race.HU);
        Assert.AreEqual("zed#1@20_GM_1v1_HU", id);
    }

    [Test]
    public void BuildId_At_SortsTagsAscending_IsOrderIndependent()
    {
        var a = ProgressionMilestone.BuildId(Tags("bob#2", "ann#1"), GateWay.Europe, GameMode.GM_2v2_AT, null);
        var b = ProgressionMilestone.BuildId(Tags("ann#1", "bob#2"), GateWay.Europe, GameMode.GM_2v2_AT, null);
        Assert.AreEqual(a, b);
        Assert.AreEqual("ann#1@20_bob#2@20_GM_2v2_AT", a);
    }

    [Test]
    public void Id_IsComputedFromComponents()
    {
        var m = ProgressionMilestone.Create(Tags("zed#1"), GateWay.Europe, GameMode.GM_1v1, Race.OC);
        Assert.AreEqual("zed#1@20_GM_1v1_OC", m.Id);
    }

    [Test]
    public void RecordWin_IncrementsTotalWinsMonotonically()
    {
        var m = ProgressionMilestone.Create(Tags("zed#1"), GateWay.Europe, GameMode.GM_1v1, Race.HU);
        Assert.AreEqual(0, m.TotalWins);
        m.RecordWin();
        m.RecordWin();
        Assert.AreEqual(2, m.TotalWins);
    }

    [Test]
    public void RecordActivity_CreatesWeekBucket_ThenIncrements_AndTracksLastPlayed()
    {
        var m = ProgressionMilestone.Create(Tags("zed#1"), GateWay.Europe, GameMode.GM_1v1, Race.HU);
        var wed = new DateTimeOffset(2026, 6, 3, 10, 0, 0, TimeSpan.Zero); // Wednesday
        var fri = new DateTimeOffset(2026, 6, 5, 22, 0, 0, TimeSpan.Zero); // same ISO week (Mon 2026-06-01)
        m.RecordActivity(wed);
        m.RecordActivity(fri);
        Assert.AreEqual(1, m.ActivityWeeks.Count);
        Assert.AreEqual(2, m.ActivityWeeks[0].Games);
        Assert.AreEqual(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), m.ActivityWeeks[0].WeekStartUtc);
        Assert.AreEqual(fri, m.LastPlayedAt);
    }

    [Test]
    public void RecordActivity_DifferentWeeks_CreateSeparateBuckets()
    {
        var m = ProgressionMilestone.Create(Tags("zed#1"), GateWay.Europe, GameMode.GM_1v1, Race.HU);
        m.RecordActivity(new DateTimeOffset(2026, 6, 3, 0, 0, 0, TimeSpan.Zero));   // week of 06-01
        m.RecordActivity(new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.Zero));  // week of 06-08
        Assert.AreEqual(2, m.ActivityWeeks.Count);
    }

    [Test]
    public void PruneActivityBefore_DropsWeeksStrictlyOlderThanCutoffWeek()
    {
        var m = ProgressionMilestone.Create(Tags("zed#1"), GateWay.Europe, GameMode.GM_1v1, Race.HU);
        m.RecordActivity(new DateTimeOffset(2026, 1, 7, 0, 0, 0, TimeSpan.Zero));   // old
        m.RecordActivity(new DateTimeOffset(2026, 6, 3, 0, 0, 0, TimeSpan.Zero));   // recent
        m.PruneActivityBefore(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.AreEqual(1, m.ActivityWeeks.Count);
        Assert.AreEqual(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), m.ActivityWeeks[0].WeekStartUtc);
    }

    [Test]
    public void ActivityIn_SumsGamesAndActiveWeeks_WithinTrailing90Days()
    {
        var m = ProgressionMilestone.Create(Tags("zed#1"), GateWay.Europe, GameMode.GM_1v1, Race.HU);
        var now = new DateTimeOffset(2026, 6, 8, 0, 0, 0, TimeSpan.Zero);
        m.RecordActivity(now.AddDays(-3));   // in window, week A
        m.RecordActivity(now.AddDays(-3));   // same week A
        m.RecordActivity(now.AddDays(-20));  // in window, week B
        m.RecordActivity(now.AddDays(-120)); // outside 90d window
        var activity = m.ActivityIn(now);
        Assert.AreEqual(3, activity.RecentGames);
        Assert.AreEqual(2, activity.ActiveWeeks);
    }

    [Test]
    public void ActivityIn_DormantPlayer_ReturnsZero()
    {
        var m = ProgressionMilestone.Create(Tags("zed#1"), GateWay.Europe, GameMode.GM_1v1, Race.HU);
        var played = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        m.RecordActivity(played);
        var now = new DateTimeOffset(2026, 6, 8, 0, 0, 0, TimeSpan.Zero); // >90d later
        var activity = m.ActivityIn(now);
        Assert.AreEqual(0, activity.RecentGames);
        Assert.AreEqual(0, activity.ActiveWeeks);
    }

    [Test]
    public void PruneActivityBefore_RetainsWeekEqualToCutoffWeek()
    {
        var m = ProgressionMilestone.Create(Tags("zed#1"), GateWay.Europe, GameMode.GM_1v1, Race.HU);
        m.RecordActivity(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)); // Mon 2026-06-01
        m.PruneActivityBefore(new DateTimeOffset(2026, 6, 3, 0, 0, 0, TimeSpan.Zero)); // cutoff week = 2026-06-01
        Assert.AreEqual(1, m.ActivityWeeks.Count);
    }

    [Test]
    public void ActivityIn_IncludesWindowStartWeek_ExcludesEarlierWeek()
    {
        var m = ProgressionMilestone.Create(Tags("zed#1"), GateWay.Europe, GameMode.GM_1v1, Race.HU);
        var now = new DateTimeOffset(2026, 6, 8, 0, 0, 0, TimeSpan.Zero); // Monday
        var windowStartWeek = ProgressionMilestone.StartOfIsoWeek(now.AddDays(-ProgressionMilestone.RecentWindowDays));
        m.RecordActivity(windowStartWeek);                 // exactly on the window-start week → included
        m.RecordActivity(windowStartWeek.AddDays(-1));     // the prior week → excluded
        var activity = m.ActivityIn(now);
        Assert.AreEqual(1, activity.RecentGames);
        Assert.AreEqual(1, activity.ActiveWeeks);
    }

    [Test]
    public void PruneStaleActivity_DropsWeeksOlderThanWindowPlusMargin_KeepsRecent()
    {
        var m = ProgressionMilestone.Create(Tags("zed#1"), GateWay.Europe, GameMode.GM_1v1, Race.HU);
        var reference = new DateTimeOffset(2026, 6, 8, 0, 0, 0, TimeSpan.Zero);
        m.RecordActivity(reference);                       // recent → kept
        m.RecordActivity(reference.AddDays(-200));         // well outside window+margin → dropped
        m.PruneStaleActivity(reference);
        Assert.AreEqual(1, m.ActivityWeeks.Count);
        Assert.AreEqual(ProgressionMilestone.StartOfIsoWeek(reference), m.ActivityWeeks[0].WeekStartUtc);
    }
}
