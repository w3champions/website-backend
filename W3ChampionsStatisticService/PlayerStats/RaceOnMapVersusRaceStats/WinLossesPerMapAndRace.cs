using System.Collections.Generic;
using W3ChampionsStatisticService.PlayerProfiles;

namespace W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats
{
    public class WinLossesPerMapAndRace
    {
        public Race Race { get; set; }
        public List<WinLossesPerMap> WinLossesOnMap { get; set; } = new List<WinLossesPerMap>();
        public static WinLossesPerMapAndRace Create(Race race)
        {
            return new WinLossesPerMapAndRace
            {
                Race = race
            };
        }
    }
}