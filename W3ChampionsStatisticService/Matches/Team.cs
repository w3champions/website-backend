using System.Collections.Generic;

namespace W3ChampionsStatisticService.Matches
{
    public class Team
    {
        public List<PlayerOverviewMatches> Players { get; set; } = new List<PlayerOverviewMatches>();
    }
}