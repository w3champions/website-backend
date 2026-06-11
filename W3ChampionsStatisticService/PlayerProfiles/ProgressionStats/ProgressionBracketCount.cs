using W3C.Contracts.Matchmaking;

namespace W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

// One (gameMode, league, division) bracket population row for the bracket-count metrics
// (division is null for leagues that have no divisions).
public class ProgressionBracketCount
{
    public GameMode GameMode { get; set; }
    public int League { get; set; }
    public int? Division { get; set; }
    public long Count { get; set; }
}
