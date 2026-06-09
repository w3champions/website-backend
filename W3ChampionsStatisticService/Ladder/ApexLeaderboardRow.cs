using System.Collections.Generic;

namespace W3ChampionsStatisticService.Ladder;

public class ApexLeaderboardRow
{
    public List<PlayerInfo> PlayersInfo { get; set; } = new List<PlayerInfo>();
    public int ApexPoints { get; set; }
    public int League { get; set; }
    public int RankNumber { get; set; }
}
