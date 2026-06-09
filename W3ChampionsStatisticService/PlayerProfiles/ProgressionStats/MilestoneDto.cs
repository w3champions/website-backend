using System;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;

namespace W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

// The win-milestone progress as served on the owner-private endpoint (GET /api/players/my-milestones).
// Carries the mode-keying fields the caller needs to attach the bar to a row (gameMode/gateWay/race —
// numeric enum ids, race null for non-race-split modes) plus the three player-facing numbers. The keying
// fields ARE included here (unlike the old PlayerMilestoneView serve-view) because this is a flat list,
// not a per-stat-row stamp. Boundary: ONLY these fields are ever exposed — never the raw activity window
// (ActivityWeeks/RecentGames/ActiveWeeks) or LastPlayedAt.
public class MilestoneDto
{
    public GameMode GameMode { get; set; }
    public GateWay GateWay { get; set; }
    public Race? Race { get; set; }
    public long CurrentWins { get; set; }
    public long PreviousTarget { get; set; }
    public long NextTarget { get; set; }

    public static MilestoneDto FromReadModel(ProgressionMilestone milestone, DateTimeOffset now)
    {
        // Reuse the pure on-read target calculation (wins + bar bounds) to stay DRY with the kept view.
        var view = PlayerMilestoneView.FromReadModel(milestone, now);
        return new MilestoneDto
        {
            GameMode = milestone.GameMode,
            GateWay = milestone.GateWay,
            Race = milestone.Race,
            CurrentWins = view.CurrentWins,
            PreviousTarget = view.PreviousTarget,
            NextTarget = view.NextTarget,
        };
    }
}
