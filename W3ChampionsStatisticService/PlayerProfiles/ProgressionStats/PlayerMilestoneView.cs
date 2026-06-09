using System;

namespace W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

// The win-milestone progress as served to clients on the PlayerGameModeStatPerGateway DTO. Carries
// only the three player-facing numbers (no keying/identity fields): the current lifetime win total
// and the bar bounds it sits between (previousTarget = current band's lower bound, nextTarget = the
// next milestone). Null when the player has no milestone record for that (mode, ...).
public class PlayerMilestoneView
{
    public long CurrentWins { get; set; }
    public long PreviousTarget { get; set; }
    public long NextTarget { get; set; }

    public static PlayerMilestoneView FromReadModel(ProgressionMilestone milestone, DateTimeOffset now)
    {
        if (milestone == null)
        {
            return null;
        }

        var target = MilestoneTargetCalculator.Compute(milestone.TotalWins, milestone.ActivityIn(now));
        return new PlayerMilestoneView
        {
            CurrentWins = milestone.TotalWins,
            PreviousTarget = target.PreviousTarget,
            NextTarget = target.NextTarget,
        };
    }
}
