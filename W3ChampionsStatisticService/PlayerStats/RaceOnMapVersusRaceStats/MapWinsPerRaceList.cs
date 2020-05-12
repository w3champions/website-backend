using System.Collections.Generic;
using System.Linq;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.PlayerProfiles;

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
                WinLossesPerMapAndRace.Create(Race.NE),
                WinLossesPerMapAndRace.Create(Race.Total)
            };
        }

        public void AddWin(Race myRace, Race enemyRace, string mapName, in bool won)
        {
            var raceResult = this.Single(r => r.Race == myRace);
            var totalReslt = this.Single(r => r.Race == Race.Total);
            var mapOfRace = WinLossesPerMap(mapName, raceResult);
            var mapOfTotal = WinLossesPerMap(mapName, totalReslt);
            mapOfRace.RecordWin(enemyRace, won);
            mapOfTotal.RecordWin(enemyRace, won);
        }

        private static WinLossesPerMap WinLossesPerMap(string mapName, WinLossesPerMapAndRace race)
        {
            var map = race.WinLossesOnMap.SingleOrDefault(m => m.Map == mapName);
            if (map == null)
            {
                race.WinLossesOnMap.Add(RaceOnMapVersusRaceStats.WinLossesPerMap.Create(mapName));
            }

            var map2 = race.WinLossesOnMap.Single(m => m.Map == mapName);
            return map2;
        }
    }
}