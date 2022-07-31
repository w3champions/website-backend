using System.Collections.Generic;
using W3C.Domain.CommonValueObjects;

namespace W3ChampionsStatisticService.PlayerProfiles.MmrRankingStats
{
    public class MmrRank
    {
        public MmrRank()
        {
            Ranks = new Dictionary<string, PlayerMmrRank>();
        }

        public GameMode GameMode { get; set; }
        public GateWay Gateway { get; set; }
        public Dictionary<string, PlayerMmrRank> Ranks { get; set; }
    }
}
