namespace W3ChampionsStatisticService.Matches;

// One result of the player-scoped opponent search: a player who shares
// finished matches with the searched player, and how many they share.
public class OpponentInfo
{
    public string BattleTag { get; set; }
    public long MatchCount { get; set; }
}
