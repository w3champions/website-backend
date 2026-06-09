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

    // Baseline curve: steps 5, 10, 25, 50, 100 — capped at 100.

    [Test]
    public void Baseline_Band0_Step5()
    {
        var t = MilestoneTargetCalculator.Compute(30, Active);
        Assert.AreEqual(35, t.NextTarget);
        Assert.AreEqual(5, t.WinsToNext);
    }

    [Test]
    public void Baseline_Band1_Step10()
    {
        var t = MilestoneTargetCalculator.Compute(64, Active);
        Assert.AreEqual(70, t.NextTarget);
        Assert.AreEqual(6, t.WinsToNext);
    }

    [Test]
    public void Baseline_Band2_Step25()
    {
        var t = MilestoneTargetCalculator.Compute(120, Active);
        Assert.AreEqual(125, t.NextTarget);
        Assert.AreEqual(5, t.WinsToNext);
    }

    [Test]
    public void Baseline_Band3_Step50()
    {
        var t = MilestoneTargetCalculator.Compute(320, Active);
        Assert.AreEqual(350, t.NextTarget);
        Assert.AreEqual(30, t.WinsToNext);
    }

    [Test]
    public void Baseline_Band4_Step100()
    {
        var t = MilestoneTargetCalculator.Compute(7300, Active);
        Assert.AreEqual(7400, t.NextTarget);
        Assert.AreEqual(100, t.WinsToNext);
    }

    [Test]
    public void MaxStep_IsCappedAt100_NoMatterHowLargeTheTotal()
    {
        var t = MilestoneTargetCalculator.Compute(100_000, Active);
        Assert.AreEqual(100_100, t.NextTarget);
        Assert.AreEqual(100, t.WinsToNext); // step never exceeds 100
    }

    [Test]
    public void NextTarget_IsAlwaysStrictlyGreater_EvenOnAMilestone()
    {
        var t = MilestoneTargetCalculator.Compute(50, Active);   // exactly on 50, now in band1 (step 10)
        Assert.AreEqual(60, t.NextTarget);
        Assert.AreEqual(10, t.WinsToNext);
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
        var baseline = MilestoneTargetCalculator.Compute(1230, Active);  // step 100 → 1300
        var dormant = MilestoneTargetCalculator.Compute(1230, Dormant);  // finest step 5 → 1235
        Assert.AreEqual(1300, baseline.NextTarget);
        Assert.AreEqual(1235, dormant.NextTarget);
        Assert.Less(dormant.NextTarget, baseline.NextTarget); // nearer
        Assert.Greater(dormant.WinsToNext, 0);
    }

    [Test]
    public void CatchUp_LowVolume_NarrowsOrEquals_NeverWidens()
    {
        var baseline = MilestoneTargetCalculator.Compute(1230, Active);    // step 100 → 1300
        var low = MilestoneTargetCalculator.Compute(1230, LowVolume);      // one band finer, step 50 → 1250
        Assert.LessOrEqual(low.NextTarget, baseline.NextTarget);
        Assert.AreEqual(1250, low.NextTarget);
        Assert.Greater(low.WinsToNext, 0);
    }

    [Test]
    public void CatchUp_FewActiveWeeks_EvenWithEnoughGames_Narrows()
    {
        var fewWeeks = new MilestoneActivity(15, 2); // RecentGames >= 10 but ActiveWeeks < 3
        var baseline = MilestoneTargetCalculator.Compute(1230, Active);    // step 100 → 1300
        var narrowed = MilestoneTargetCalculator.Compute(1230, fewWeeks);  // one band finer, step 50 → 1250
        Assert.Less(narrowed.NextTarget, baseline.NextTarget);
        Assert.Greater(narrowed.WinsToNext, 0);
    }

    // PreviousTarget is the lower bound of the current band's bar: the largest milestone <= totalWins
    // on the baseline curve. It depends only on totalWins, never on the activity input. Invariant:
    // PreviousTarget <= totalWins < NextTarget.
    [TestCase(0, 0, 5)]
    [TestCase(3, 0, 5)]
    [TestCase(5, 5, 10)]
    [TestCase(53, 50, 60)]
    [TestCase(247, 225, 250)]
    [TestCase(640, 600, 700)]
    public void Compute_SetsPreviousAndNextTarget(long totalWins, long expectedPrev, long expectedNext)
    {
        // Active profile so the upper target sits on the baseline curve. PreviousTarget is the band lower
        // bound and is activity-independent, so these expected values hold for any activity input.
        var t = MilestoneTargetCalculator.Compute(totalWins, Active);
        Assert.That(t.PreviousTarget, Is.EqualTo(expectedPrev));
        Assert.That(t.NextTarget, Is.EqualTo(expectedNext));
        Assert.That(t.PreviousTarget, Is.LessThanOrEqualTo(totalWins));
        Assert.That(totalWins, Is.LessThan(t.NextTarget));
    }

    [Test]
    public void PreviousTarget_IsLargestMilestoneNotAbove_AndBelowNextTarget_AcrossSamples()
    {
        long[] totals = { 0, 7, 49, 50, 64, 99, 100, 213, 249, 250, 300, 499, 500, 1230, 7300, 99999 };
        foreach (var total in totals)
        {
            foreach (var activity in new[] { Active, LowVolume, Dormant })
            {
                var t = MilestoneTargetCalculator.Compute(total, activity);
                Assert.LessOrEqual(t.PreviousTarget, total, $"previousTarget above total at {total}");
                Assert.Less(t.PreviousTarget, t.NextTarget, $"previousTarget not below nextTarget at {total}");
            }
        }
    }

    [Test]
    public void Invariant_CatchUpNeverWidens_AcrossSamples()
    {
        long[] totals = { 0, 7, 49, 50, 64, 99, 100, 213, 249, 250, 300, 499, 500, 1230, 7300, 99999 };
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
