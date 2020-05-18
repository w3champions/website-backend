using System.Collections.Generic;
using W3ChampionsStatisticService.CommonValueObjects;

namespace W3ChampionsStatisticService.PlayerProfiles._2v2Stats
{
    public class At2V2StatsPerGateway : BaseRankedStat
    {
        public static At2V2StatsPerGateway Create(BattleTagIdCombined id)
        {
            return new At2V2StatsPerGateway
            {
                Id = id.Id,
                Season = id.Season,
                PlayerIds = id.BattleTags
            };
        }

        public List<PlayerId> PlayerIds { get; set; }

        public int Season { get; set; }

        public string Id { get; set; }
    }
}