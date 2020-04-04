using System.Collections.Generic;
using System.Linq;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapStats;

namespace W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats
{
    public class MapWinsPerRaceList : List<WinLossesPerMapAndRace>
    {
        public static MapWinsPerRaceList Create()
        {
            return new MapWinsPerRaceList
            {
                WinLossesPerMapAndRace.Create(Race.RnD),
                WinLossesPerMapAndRace.Create(Race.HU),
                WinLossesPerMapAndRace.Create(Race.OC),
                WinLossesPerMapAndRace.Create(Race.UD),
                WinLossesPerMapAndRace.Create(Race.NE)
            };
        }

        public void AddWin(Race myRace, Race enemyRace, string mapName, in bool won)
        {
            var race = this.Single(r => r.Race == myRace);
            var map = race.WinLossesOnMap.SingleOrDefault(m => m.Map == mapName);
            if (map == null)
            {
                race.WinLossesOnMap.Add(WinLossesPerMap.Create(mapName));
            }
            var map2 = race.WinLossesOnMap.Single(m => m.Map == mapName);
            map2.RecordWin(enemyRace, won);
        }
    }
}