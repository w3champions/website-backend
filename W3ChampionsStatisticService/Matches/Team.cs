using System.Collections.Generic;
using System.Linq;

namespace W3ChampionsStatisticService.Matches;

public class Team
{
    public List<PlayerOverviewMatches> Players { get; set; } = new List<PlayerOverviewMatches>();
    public bool Won => Players?.Any(x => x.Won) ?? false;
    public int? MatchRanking { get; set; }
}
