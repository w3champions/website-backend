using System;
using System.Linq;
using NUnit.Framework;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

namespace WC3ChampionsStatisticService.UnitTests.PlayerProfiles.ProgressionStats;

[TestFixture]
public class ProgressionPrestigeTests
{
    private static PeakRank Rank(int league, int division, int points, int season) =>
        new() { League = league, Division = division, Points = points, Season = season, AchievedAt = DateTimeOffset.UnixEpoch };

    [Test]
    public void FirstRecord_SetsBothAllTimeAndSeasonPeak()
    {
        var p = ProgressionPrestige.Create("peak#1");
        p.RecordPeak(GameMode.GM_1v1, Race.HU, Rank(5, 2, 30, season: 1));

        var entry = p.Peaks.Single();
        Assert.AreEqual(GameMode.GM_1v1, entry.GameMode);
        Assert.AreEqual(Race.HU, entry.Race);
        Assert.AreEqual(5, entry.AllTimePeak.League);
        Assert.AreEqual(1, entry.SeasonPeaks.Single().Season);
        Assert.IsEmpty(entry.Badges);
    }

    [Test]
    public void LowerSubsequentRank_DoesNotLowerEitherPeak()
    {
        var p = ProgressionPrestige.Create("peak#1");
        p.RecordPeak(GameMode.GM_1v1, Race.HU, Rank(3, 1, 50, season: 1)); // Diamond I
        p.RecordPeak(GameMode.GM_1v1, Race.HU, Rank(5, 4, 10, season: 1)); // dropped to Gold IV

        var entry = p.Peaks.Single();
        Assert.AreEqual(3, entry.AllTimePeak.League);          // still Diamond
        Assert.AreEqual(3, entry.SeasonPeaks.Single().League); // season peak also unchanged
    }

    [Test]
    public void HigherSubsequentRank_RaisesBothPeaks()
    {
        var p = ProgressionPrestige.Create("peak#1");
        p.RecordPeak(GameMode.GM_1v1, Race.HU, Rank(5, 2, 30, season: 1));
        p.RecordPeak(GameMode.GM_1v1, Race.HU, Rank(3, 2, 30, season: 1)); // climbed to Diamond

        var entry = p.Peaks.Single();
        Assert.AreEqual(3, entry.AllTimePeak.League);
        Assert.AreEqual(3, entry.SeasonPeaks.Single().League);
    }

    [Test]
    public void NewSeason_AppendsSeasonPeak_AllTimeSurvivesAcrossSeasons()
    {
        var p = ProgressionPrestige.Create("peak#1");
        p.RecordPeak(GameMode.GM_1v1, Race.HU, Rank(3, 1, 50, season: 1)); // Diamond I in S1
        p.RecordPeak(GameMode.GM_1v1, Race.HU, Rank(6, 3, 20, season: 2)); // Silver III in S2 (reset)

        var entry = p.Peaks.Single();
        Assert.AreEqual(2, entry.SeasonPeaks.Count);
        Assert.AreEqual(3, entry.SeasonPeaks.Single(s => s.Season == 1).League);
        Assert.AreEqual(6, entry.SeasonPeaks.Single(s => s.Season == 2).League);
        Assert.AreEqual(3, entry.AllTimePeak.League);          // all-time = the S1 Diamond
        // Invariant: all-time == best of the per-season peaks
        var best = entry.SeasonPeaks.Aggregate((a, b) => PrestigeRankComparer.IsHigher(a, b) ? a : b);
        Assert.AreEqual(best.League, entry.AllTimePeak.League);
    }

    [Test]
    public void DistinctRace_ProducesSeparateEntries()
    {
        var p = ProgressionPrestige.Create("peak#1");
        p.RecordPeak(GameMode.GM_1v1, Race.HU, Rank(5, 2, 30, season: 1));
        p.RecordPeak(GameMode.GM_1v1, Race.OC, Rank(4, 2, 30, season: 1));
        Assert.AreEqual(2, p.Peaks.Count);
    }
}
