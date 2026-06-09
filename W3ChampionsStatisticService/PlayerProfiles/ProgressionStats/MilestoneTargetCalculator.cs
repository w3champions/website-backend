using System;

namespace W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

// Pure, on-read next-milestone selection. Baseline granularity curve (the next round-number of
// wins; the step coarsens as totals grow, capped at 100) plus a player-local catch-up that only
// ever NARROWS the gap (a returning/low-volume player gets a nearer milestone; an active player
// gets the baseline — never a widening gap). All thresholds are tunable constants; tests assert
// behaviour + invariants.
public static class MilestoneTargetCalculator
{
    // (upperBoundExclusive, step). Step sizes are 5, 10, 25, 50, 100 — capped at 100. They are NOT
    // all integer multiples of one another (25 is not a multiple of 10), so a finer catch-up step can
    // occasionally land beyond the baseline milestone; Compute clamps the catch-up to the baseline so
    // it can never widen the gap regardless of the step set.
    private static readonly (long UpperExclusive, long Step)[] Bands =
    {
        (50, 5),
        (100, 10),
        (250, 25),
        (500, 50),
        (long.MaxValue, 100),
    };

    // Catch-up thresholds over the trailing recent-activity window (tunable).
    private const int DormantRecentGames = 0;       // no games in window → finest grid
    private const int LowVolumeRecentGames = 10;    // few games in window → one band finer
    private const int LowVolumeActiveWeeks = 3;     // …or active in few weeks

    public static MilestoneTarget Compute(long totalWins, MilestoneActivity activity)
    {
        var baselineBand = BaselineBandIndex(totalWins);
        var baselineStep = Bands[baselineBand].Step;
        var baselineTarget = NextMultiple(totalWins, baselineStep);

        var catchUpBand = ApplyCatchUp(baselineBand, activity);
        var catchUpTarget = NextMultiple(totalWins, Bands[catchUpBand].Step);

        // Catch-up may only narrow. Because the step set is not fully nested, a finer step can land
        // beyond the baseline milestone, so clamp to the baseline — never widen.
        var next = Math.Min(baselineTarget, catchUpTarget);

        // The lower bound of the current band's bar: the largest baseline milestone not above totalWins
        // (0 below the first step). Depends only on totalWins, never on the activity input.
        var previous = PreviousMultiple(totalWins, baselineStep);
        return new MilestoneTarget(previous, next, next - totalWins);
    }

    // The next multiple of step strictly greater than totalWins.
    private static long NextMultiple(long totalWins, long step) => ((totalWins / step) + 1) * step;

    // The largest multiple of step not greater than totalWins (the band's lower bound).
    private static long PreviousMultiple(long totalWins, long step) => (totalWins / step) * step;

    private static int BaselineBandIndex(long totalWins)
    {
        for (var i = 0; i < Bands.Length; i++)
        {
            if (totalWins < Bands[i].UpperExclusive)
            {
                return i;
            }
        }
        // Reached only when totalWins == long.MaxValue (loop's strict < misses the final band); returns the top band correctly.
        return Bands.Length - 1;
    }

    private static int ApplyCatchUp(int baseBand, MilestoneActivity activity)
    {
        var finer = 0;
        if (activity.RecentGames <= DormantRecentGames) // <= also treats any negative as dormant (structurally impossible from ActivityIn, but Compute is public)
        {
            finer = baseBand; // drop all the way to the finest (band 0)
        }
        else if (activity.RecentGames < LowVolumeRecentGames || activity.ActiveWeeks < LowVolumeActiveWeeks)
        {
            finer = 1;
        }
        var band = baseBand - finer;
        return band < 0 ? 0 : band;
    }
}
