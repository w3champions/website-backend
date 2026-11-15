namespace W3ChampionsStatisticService.Matches;

// One result of the player-scoped opponent search: a player who shares
// finished matches with the searched player, how many they share, and the
// searched player's record across them (allies share the same result).
public class OpponentInfo
{
    public string BattleTag { get; set; }
    public long MatchCount { get; set; }
    public long Wins { get; set; }
    public long Losses { get; set; }
}
