namespace W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

// Pure, on-read next-milestone selection. Baseline granularity curve (the next round-number of
// wins; the step coarsens as totals grow) plus a player-local catch-up that only ever NARROWS the
// gap (a returning/low-volume player gets a nearer milestone; an active player gets the baseline —
// never a widening gap). All thresholds are tunable constants; tests assert behaviour + invariants.
public static class MilestoneTargetCalculator
{
    // (upperBoundExclusive, step). Coarser steps are integer multiples of finer ones so a finer
    // catch-up step never produces a farther target.
    private static readonly (long UpperExclusive, long Step)[] Bands =
    {
        (50, 5),
        (500, 50),
        (5000, 250),
        (long.MaxValue, 1000),
    };

    // Catch-up thresholds over the trailing recent-activity window (tunable).
    private const int DormantRecentGames = 0;       // no games in window → finest grid
    private const int LowVolumeRecentGames = 10;    // few games in window → one band finer
    private const int LowVolumeActiveWeeks = 3;     // …or active in few weeks

    public static MilestoneTarget Compute(long totalWins, MilestoneActivity activity)
    {
        var baseBand = BaselineBandIndex(totalWins);
        var band = ApplyCatchUp(baseBand, activity);
        var step = Bands[band].Step;
        var next = ((totalWins / step) + 1) * step; // strictly greater multiple of step
        return new MilestoneTarget(next, next - totalWins);
    }

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
