using System.Collections.Generic;
using W3ChampionsStatisticService.CommonValueObjects;

namespace W3ChampionsStatisticService.PlayerProfiles
{
    public class GameModeStatsPerGateway
    {
        public static GameModeStatsPerGateway Create(GateWay gateway, int season)
        {
            return new GameModeStatsPerGateway
            {
                GateWay = gateway,
                Season = season,
                GameModeStats = new List<GameModeStat>()
                {
                    new GameModeStat(GameMode.GM_1v1),
                    new GameModeStat(GameMode.GM_2v2_AT),
                    new GameModeStat(GameMode.GM_4v4),
                    new GameModeStat(GameMode.FFA)
                }
            };
        }

        public GateWay GateWay { get; set; }

        public List<GameModeStat> GameModeStats { get; set; }
        public int Season { get; set; }
    }
}