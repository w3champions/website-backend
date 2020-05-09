using System.Collections.Generic;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Ladder
{
    public class LeagueConstellation : IIdentifiable
    {
        public string Id { get; set; }
        public GateWay Gateway { get; set; }
        public GameMode GameMode { get; set; }
        public List<League> Leagues { get; set; }
    }
}