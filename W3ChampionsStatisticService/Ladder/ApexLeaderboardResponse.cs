using System.Collections.Generic;

namespace W3ChampionsStatisticService.Ladder;

public class ApexLeaderboardResponse
{
    public int? CutoffApexPoints { get; set; }
    public int GmCount { get; set; }
    public List<ApexLeaderboardRow> Players { get; set; } = new List<ApexLeaderboardRow>();
}
