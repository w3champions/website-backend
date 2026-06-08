using NUnit.Framework;
using W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

namespace WC3ChampionsStatisticService.UnitTests.PlayerProfiles.ProgressionStats;

[TestFixture]
public class MilestoneTargetCalculatorTests
{
    // An "active" profile: enough recent games/weeks to sit at the baseline curve.
    private static readonly MilestoneActivity Active = new(40, 8);
    private static readonly MilestoneActivity LowVolume = new(6, 3);
    private static readonly MilestoneActivity Dormant = new(0, 0);

    [Test]
    public void Baseline_Band0_Step5()
    {
        var t = MilestoneTargetCalculator.Compute(30, Active);
        Assert.AreEqual(35, t.NextTarget);
        Assert.AreEqual(5, t.WinsToNext);
    }

    [Test]
    public void Baseline_Band1_Step50()
    {
        var t = MilestoneTargetCalculator.Compute(220, Active);
        Assert.AreEqual(250, t.NextTarget);
        Assert.AreEqual(30, t.WinsToNext);
    }

    [Test]
    public void Baseline_Band2_Step250()
    {
        var t = MilestoneTargetCalculator.Compute(1230, Active);
        Assert.AreEqual(1250, t.NextTarget);
        Assert.AreEqual(20, t.WinsToNext);
    }

    [Test]
    public void Baseline_Band3_Step1000()
    {
        var t = MilestoneTargetCalculator.Compute(7300, Active);
        Assert.AreEqual(8000, t.NextTarget);
        Assert.AreEqual(700, t.WinsToNext);
    }

    [Test]
    public void NextTarget_IsAlwaysStrictlyGreater_EvenOnAMilestone()
    {
        var t = MilestoneTargetCalculator.Compute(50, Active);   // exactly on 50, now in band1 (step 50)
        Assert.AreEqual(100, t.NextTarget);
        Assert.AreEqual(50, t.WinsToNext);
        Assert.Greater(t.WinsToNext, 0);
    }

    [Test]
    public void Newcomer_LowTotals_GetFineStepsFromCurveAlone()
    {
        var t = MilestoneTargetCalculator.Compute(7, Active);
        Assert.AreEqual(10, t.NextTarget); // step 5 grid
    }

    [Test]
    public void CatchUp_Dormant_NarrowsTargetVsBaseline()
    {
        var baseline = MilestoneTargetCalculator.Compute(1230, Active);
        var dormant = MilestoneTargetCalculator.Compute(1230, Dormant);
        Assert.Less(dormant.NextTarget, baseline.NextTarget); // nearer
        Assert.Greater(dormant.WinsToNext, 0);
    }

    [Test]
    public void CatchUp_LowVolume_NarrowsOrEquals_NeverWidens()
    {
        var baseline = MilestoneTargetCalculator.Compute(1230, Active);
        var low = MilestoneTargetCalculator.Compute(1230, LowVolume);
        Assert.LessOrEqual(low.NextTarget, baseline.NextTarget);
        Assert.Greater(low.WinsToNext, 0);
        // 1230 is a multiple of 50 as well as 250, so the low-volume (band1, step50) target equals the baseline here — narrows-or-equals, never widens.
    }

    [Test]
    public void CatchUp_FewActiveWeeks_EvenWithEnoughGames_Narrows()
    {
        var fewWeeks = new MilestoneActivity(15, 2); // RecentGames >= 10 but ActiveWeeks < 3
        var baseline = MilestoneTargetCalculator.Compute(1260, Active);
        var narrowed = MilestoneTargetCalculator.Compute(1260, fewWeeks);
        Assert.Less(narrowed.NextTarget, baseline.NextTarget); // band2 step250 → 1500; band1 step50 → 1300 (narrower)
        Assert.Greater(narrowed.WinsToNext, 0);
    }

    [Test]
    public void Invariant_CatchUpNeverWidens_AcrossSamples()
    {
        long[] totals = { 0, 7, 49, 50, 213, 499, 500, 1230, 4999, 5000, 7300, 99999 };
        foreach (var total in totals)
        {
            var baseline = MilestoneTargetCalculator.Compute(total, Active);
            var dormant = MilestoneTargetCalculator.Compute(total, Dormant);
            var low = MilestoneTargetCalculator.Compute(total, LowVolume);
            Assert.LessOrEqual(dormant.NextTarget, baseline.NextTarget, $"dormant widened at {total}");
            Assert.LessOrEqual(low.NextTarget, baseline.NextTarget, $"low widened at {total}");
            foreach (var t in new[] { baseline, dormant, low })
            {
                Assert.Greater(t.NextTarget, total, $"target not strictly greater at {total}");
                Assert.AreEqual(t.NextTarget - total, t.WinsToNext, $"winsToNext mismatch at {total}");
            }
        }
    }
}
