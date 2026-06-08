using NUnit.Framework;
using W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

namespace WC3ChampionsStatisticService.UnitTests.PlayerProfiles.ProgressionStats;

[TestFixture]
public class PrestigeRankComparerTests
{
    private static PeakRank Steered(int league, int division, int points, int season = 1) =>
        new() { League = league, Division = division, Points = points, ApexPoints = null, Season = season };

    private static PeakRank Apex(int apexPoints, int league = 1, int season = 1) =>
        new() { League = league, Division = 0, Points = 0, ApexPoints = apexPoints, Season = season };

    [Test]
    public void LowerLeagueIsHigherRank()
    {
        // Diamond (league 3) outranks Gold (league 5)
        Assert.IsTrue(PrestigeRankComparer.IsHigher(Steered(3, 4, 0), Steered(5, 1, 99)));
        Assert.IsFalse(PrestigeRankComparer.IsHigher(Steered(5, 1, 99), Steered(3, 4, 0)));
    }

    [Test]
    public void WithinLeagueLowerDivisionIsBetter()
    {
        // Division I (1) beats Division IV (4) in the same league
        Assert.IsTrue(PrestigeRankComparer.IsHigher(Steered(4, 1, 0), Steered(4, 4, 99)));
        Assert.IsFalse(PrestigeRankComparer.IsHigher(Steered(4, 4, 99), Steered(4, 1, 0)));
    }

    [Test]
    public void WithinDivisionMorePointsIsBetter()
    {
        Assert.IsTrue(PrestigeRankComparer.IsHigher(Steered(4, 2, 80), Steered(4, 2, 20)));
        Assert.IsFalse(PrestigeRankComparer.IsHigher(Steered(4, 2, 20), Steered(4, 2, 80)));
    }

    [Test]
    public void EqualRankIsNotHigher()
    {
        Assert.IsFalse(PrestigeRankComparer.IsHigher(Steered(4, 2, 50), Steered(4, 2, 50)));
    }

    [Test]
    public void ApexBeatsAnySteered()
    {
        // even the very top steered rank (Adept league 2, Div I, 99) loses to any apex
        Assert.IsTrue(PrestigeRankComparer.IsHigher(Apex(1), Steered(2, 1, 99)));
        Assert.IsFalse(PrestigeRankComparer.IsHigher(Steered(2, 1, 99), Apex(1)));
    }

    [Test]
    public void WithinApexMoreApexPointsIsBetter()
    {
        Assert.IsTrue(PrestigeRankComparer.IsHigher(Apex(500), Apex(100)));
        Assert.IsFalse(PrestigeRankComparer.IsHigher(Apex(100), Apex(500)));
    }

    [Test]
    public void ApexPointsTieBreaksOnLowerLeague()
    {
        // GrandMaster (0) beats Master (1) at equal apexPoints
        Assert.IsTrue(PrestigeRankComparer.IsHigher(Apex(200, league: 0), Apex(200, league: 1)));
        Assert.IsFalse(PrestigeRankComparer.IsHigher(Apex(200, league: 1), Apex(200, league: 0)));
    }

    [Test]
    public void NullLeagueCandidateIsNeverHigher()
    {
        var noRank = new PeakRank { League = null };
        Assert.IsFalse(PrestigeRankComparer.IsHigher(noRank, Steered(8, 4, 0)));
    }

    [Test]
    public void AnyPlacedRankBeatsNullCurrent()
    {
        var noRank = new PeakRank { League = null };
        Assert.IsTrue(PrestigeRankComparer.IsHigher(Steered(8, 4, 0), noRank));
    }
}
