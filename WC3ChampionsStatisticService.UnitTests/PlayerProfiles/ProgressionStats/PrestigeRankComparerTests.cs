using NUnit.Framework;
using W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

namespace WC3ChampionsStatisticService.UnitTests.PlayerProfiles.ProgressionStats;

[TestFixture]
public class PrestigeRankComparerTests
{
    private static PeakRank Steered(int league, int division, int points, int season = 1) =>
        new() { League = league, Division = division, Points = points, ApexPoints = null, Season = season };

    // apex: league=Master/GM, division/points unused by the comparator (engine uses division 0 for apex).
    private static PeakRank Apex(int apexPoints, int league = 1, int season = 1) =>
        new() { League = league, Division = 0, Points = 0, ApexPoints = apexPoints, Season = season };

    [Test]
    public void LowerLeagueIsHigherRank()
    {
        // Diamond (league 3) outranks Gold (league 5)
        Assert.That(PrestigeRankComparer.IsHigher(Steered(3, 4, 0), Steered(5, 1, 99)), Is.True);
        Assert.That(PrestigeRankComparer.IsHigher(Steered(5, 1, 99), Steered(3, 4, 0)), Is.False);
    }

    [Test]
    public void WithinLeagueLowerDivisionIsBetter()
    {
        // Division I (1) beats Division IV (4) in the same league
        Assert.That(PrestigeRankComparer.IsHigher(Steered(4, 1, 0), Steered(4, 4, 99)), Is.True);
        Assert.That(PrestigeRankComparer.IsHigher(Steered(4, 4, 99), Steered(4, 1, 0)), Is.False);
    }

    [Test]
    public void WithinDivisionMorePointsIsBetter()
    {
        Assert.That(PrestigeRankComparer.IsHigher(Steered(4, 2, 80), Steered(4, 2, 20)), Is.True);
        Assert.That(PrestigeRankComparer.IsHigher(Steered(4, 2, 20), Steered(4, 2, 80)), Is.False);
    }

    [Test]
    public void EqualRankIsNotHigher()
    {
        Assert.That(PrestigeRankComparer.IsHigher(Steered(4, 2, 50), Steered(4, 2, 50)), Is.False);
    }

    [Test]
    public void ApexBeatsAnySteered()
    {
        // even the top steered rank (league 2, Div I, 99) loses to any apex
        Assert.That(PrestigeRankComparer.IsHigher(Apex(1), Steered(2, 1, 99)), Is.True);
        Assert.That(PrestigeRankComparer.IsHigher(Steered(2, 1, 99), Apex(1)), Is.False);
    }

    [Test]
    public void WithinApexMoreApexPointsIsBetter()
    {
        Assert.That(PrestigeRankComparer.IsHigher(Apex(500), Apex(100)), Is.True);
        Assert.That(PrestigeRankComparer.IsHigher(Apex(100), Apex(500)), Is.False);
    }

    [Test]
    public void ApexPointsTieBreaksOnLowerLeague()
    {
        // GrandMaster (0) beats Master (1) at equal apexPoints
        Assert.That(PrestigeRankComparer.IsHigher(Apex(200, league: 0), Apex(200, league: 1)), Is.True);
        Assert.That(PrestigeRankComparer.IsHigher(Apex(200, league: 1), Apex(200, league: 0)), Is.False);
    }

    [Test]
    public void NullLeagueCandidateIsNeverHigher()
    {
        var noRank = new PeakRank { League = null };
        Assert.That(PrestigeRankComparer.IsHigher(noRank, Steered(8, 4, 0)), Is.False);
    }

    [Test]
    public void NullCandidateReferenceIsNeverHigher()
    {
        Assert.That(PrestigeRankComparer.IsHigher(null, Steered(8, 4, 0)), Is.False);
    }

    [Test]
    public void AnyPlacedRankBeatsNullCurrent()
    {
        var noRank = new PeakRank { League = null };
        Assert.That(PrestigeRankComparer.IsHigher(Steered(8, 4, 0), noRank), Is.True);
    }
}
