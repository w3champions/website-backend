using System.Collections.Generic;

namespace W3ChampionsStatisticService.Matches
{
    public class Team
    {
        public List<PlayerOverview> Players { get; set; } = new List<PlayerOverview>();
    }
}