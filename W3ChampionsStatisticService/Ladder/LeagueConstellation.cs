using System.Collections.Generic;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PlayerProfiles;

namespace W3ChampionsStatisticService.Ladder
{
    public class LeagueConstellation
    {
        public string Id { get; set; }
        public GateWay Gateway { get; set; }
        public GameMode GameMode { get; set; }
        public List<League> Leagues { get; set; }
    }
}