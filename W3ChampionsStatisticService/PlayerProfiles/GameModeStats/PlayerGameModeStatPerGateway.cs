using System.Collections.Generic;
using W3ChampionsStatisticService.CommonValueObjects;

namespace W3ChampionsStatisticService.PlayerProfiles.GameModeStats
{
    public class PlayerGameModeStatPerGateway : BaseRankedStat
    {
        public static PlayerGameModeStatPerGateway Create(BattleTagIdCombined id)
        {
            return new PlayerGameModeStatPerGateway
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