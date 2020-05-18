using System.Collections.Generic;
using W3ChampionsStatisticService.CommonValueObjects;

namespace W3ChampionsStatisticService.PlayerProfiles._2v2Stats
{
    public class GameModeStatPerGateway : BaseRankedStat
    {
        public static GameModeStatPerGateway Create(BattleTagIdCombined id)
        {
            return new GameModeStatPerGateway
            {
                Id = id.Id,
                Season = id.Season,
                GateWay = id.GateWay,
                GameMode = id.GameMode,
                PlayerIds = id.BattleTags
            };
        }

        public GameMode GameMode { get; set; }

        public GateWay GateWay { get; set; }

        public List<PlayerId> PlayerIds { get; set; }

        public int Season { get; set; }

        public string Id { get; set; }
    }
}