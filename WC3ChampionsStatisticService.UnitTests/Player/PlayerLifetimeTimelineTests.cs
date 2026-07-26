using System;
using System.Linq;
using NUnit.Framework;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.PlayerProfiles.MmrRankingStats;

namespace WC3ChampionsStatisticService.Tests.Player;

[TestFixture]
public class PlayerLifetimeTimelineTests
{
    private static readonly DateTimeOffset Day = new(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static SeasonTimeline Season(int season, Race race, int schemaVersion, params MmrRpAtDate[] entries) =>
        Season(GameMode.GM_1v1, season, race, schemaVersion, entries);

    private static SeasonTimeline Season(GameMode gameMode, int season, Race race, int schemaVersion, params MmrRpAtDate[] entries)
    {
        var timeline = new PlayerMmrRpTimeline("peter#123", race, GateWay.Europe, season, gameMode)
        {
            SchemaVersion = schemaVersion,
        };
        timeline.MmrRpAtDates.AddRange(entries);
        return new SeasonTimeline(season, race, timeline);
    }

    private static MmrRpAtDate Entry(int dayOffset, int mmr, double? rd = null, int? dailyMax = null) =>
        new(mmr, rp: null, date: Day.AddDays(dayOffset), rd: rd, games: null, dailyMaxMmr: dailyMax);

    [Test]
    public void Build_JoinsSeasonsIntoOneSeriesPerRace()
    {
        var lifetime = PlayerLifetimeTimeline.Build(GameMode.GM_1v1,
        [
            Season(2, Race.OC, 1, Entry(10, 1600)),
            Season(1, Race.OC, 1, Entry(0, 1500), Entry(1, 1550)),
            Season(1, Race.HU, 1, Entry(0, 1400)),
        ]);

        Assert.AreEqual(2, lifetime.Series.Count);

        var orc = lifetime.Series.Single(s => s.Race == Race.OC);
        Assert.AreEqual(3, orc.Points.Count, "both seasons feed one continuous series");
        CollectionAssert.AreEqual(new[] { 1500, 1550, 1600 }, orc.Points.Select(p => p.Mmr).ToList(),
            "ordered by date, not by the order the seasons were loaded");
    }

    [Test]
    public void Build_DerivesSeasonBoundariesFromTheData()
    {
        var lifetime = PlayerLifetimeTimeline.Build(GameMode.GM_1v1,
        [
            Season(1, Race.OC, 1, Entry(0, 1500), Entry(5, 1550)),
            Season(2, Race.OC, 1, Entry(10, 1600), Entry(20, 1650)),
        ]);

        Assert.AreEqual(2, lifetime.Seasons.Count);
        Assert.AreEqual(Day, lifetime.Seasons[0].Start);
        Assert.AreEqual(Day.AddDays(5), lifetime.Seasons[0].End);
        Assert.AreEqual(Day.AddDays(20), lifetime.Seasons[1].End);
    }

    [Test]
    public void Build_ExcludesCalibratingEntriesFromThePeak()
    {
        // The highest rating was set while the system was still unsure of it.
        var lifetime = PlayerLifetimeTimeline.Build(GameMode.GM_1v1,
        [
            Season(1, Race.OC, 1,
                Entry(0, 2000, rd: 400),
                Entry(1, 1800),
                Entry(2, 1900)),
        ]);

        var peak = lifetime.Series.Single().Peak;
        Assert.AreEqual(1900, peak.Mmr, "2000 was a placement rating");
        Assert.AreEqual(Day.AddDays(2), peak.Date);
        Assert.AreEqual(1, peak.Season);
    }

    [Test]
    public void Build_PrefersTheIntraDayPeakOverTheClosingRating()
    {
        var lifetime = PlayerLifetimeTimeline.Build(GameMode.GM_1v1,
        [
            Season(1, Race.OC, 1, Entry(0, 1800, dailyMax: 1950)),
        ]);

        Assert.AreEqual(1950, lifetime.Series.Single().Peak.Mmr);
    }

    [Test]
    public void Build_ReportsNoPeakWhenHistoryPredatesCalibrationData()
    {
        // Without rd there is no way to tell placement games apart, and ratings
        // start at 1500, so an ungated peak would be wrong rather than rough.
        var lifetime = PlayerLifetimeTimeline.Build(GameMode.GM_1v1,
        [
            Season(1, Race.OC, 0, Entry(0, 1500), Entry(1, 1400)),
        ]);

        Assert.IsNull(lifetime.Series.Single().Peak);
        Assert.AreEqual(2, lifetime.Series.Single().Points.Count, "the series itself is still returned");
    }

    [Test]
    public void Build_ReportsNoPeakWhenOnlyPartOfTheHistoryIsMigrated()
    {
        var lifetime = PlayerLifetimeTimeline.Build(GameMode.GM_1v1,
        [
            Season(1, Race.OC, 0, Entry(0, 2500)),
            Season(2, Race.OC, 1, Entry(10, 1600)),
        ]);

        Assert.IsNull(lifetime.Series.Single().Peak,
            "a peak over the migrated part only would silently ignore the older seasons");
    }

    [Test]
    public void Build_MergesRacesOutsideRaceSplitModes()
    {
        // Direct Strike carries one rating whatever race is picked, so a race
        // change mid-season must not split the history in two.
        var lifetime = PlayerLifetimeTimeline.Build(GameMode.GM_DS,
        [
            Season(GameMode.GM_DS, 25, Race.UD, 1, Entry(0, 2800), Entry(1, 3057)),
            Season(GameMode.GM_DS, 25, Race.RnD, 1, Entry(2, 3103), Entry(3, 3069)),
        ]);

        Assert.AreEqual(1, lifetime.Series.Count, "one rating means one line");
        var series = lifetime.Series.Single();
        Assert.AreEqual(Race.Total, series.Race, "the combined series is not attributed to a race");
        CollectionAssert.AreEqual(new[] { 2800, 3057, 3103, 3069 }, series.Points.Select(p => p.Mmr).ToList());
        Assert.AreEqual(3103, series.Peak.Mmr, "the peak spans the race change");
    }

    [Test]
    public void Build_KeepsRacesApartInRaceSplitModes()
    {
        var lifetime = PlayerLifetimeTimeline.Build(GameMode.GM_1v1,
        [
            Season(1, Race.OC, 1, Entry(0, 2000)),
            Season(1, Race.HU, 1, Entry(1, 1800)),
        ]);

        Assert.AreEqual(2, lifetime.Series.Count, "1v1 rates each race separately");
    }

    [Test]
    public void Build_IgnoresEmptyTimelines()
    {
        var lifetime = PlayerLifetimeTimeline.Build(GameMode.GM_1v1,
        [
            Season(1, Race.OC, 1),
            Season(2, Race.OC, 1, Entry(0, 1500)),
        ]);

        Assert.AreEqual(1, lifetime.Series.Count);
        Assert.AreEqual(1, lifetime.Seasons.Count);
        Assert.AreEqual(2, lifetime.Seasons.Single().Season);
    }
}
