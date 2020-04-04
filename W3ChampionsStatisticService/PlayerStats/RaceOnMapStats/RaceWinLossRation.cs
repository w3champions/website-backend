using System.Collections.Generic;
using W3ChampionsStatisticService.PlayerProfiles;

namespace W3ChampionsStatisticService.PlayerStats.RaceOnMapStats
{
    public class MapWinLossRation
    {
        public Race Race { get; set; }
        public List<MapWinLoss> WinLosses { get; set; }
        public static MapWinLossRation Create(Race race)
        {
            return new MapWinLossRation
            {
                Race = race,
                WinLosses = new List<MapWinLoss>()
            };
        }
    }
}