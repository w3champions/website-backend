using System.Collections.Generic;
using System.Linq;
using W3C.Contracts.GameObjects;

namespace W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;

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

    public void AddWin(Race myRace, Race enemyRace, string map, int? mapId, string mapName, in bool won)
    {
        var raceResult = this.Single(r => r.Race == myRace);
        var totalReslt = this.Single(r => r.Race == Race.Total);
        var mapOfRace = WinLossesPerMap(map, raceResult);
        var mapOfTotal = WinLossesPerMap(map, totalReslt);
        mapOfRace.UpdateMapInfo(mapId, mapName);
        mapOfTotal.UpdateMapInfo(mapId, mapName);
        mapOfRace.RecordWin(enemyRace, won);
        mapOfTotal.RecordWin(enemyRace, won);
    }

    private static WinLossesPerMap WinLossesPerMap(string mapKey, WinLossesPerMapAndRace race)
    {
        var map = race.WinLossesOnMap.SingleOrDefault(m => m.Map == mapKey);
        if (map == null)
        {
            race.WinLossesOnMap.Add(RaceOnMapVersusRaceStats.WinLossesPerMap.Create(mapKey));
        }

        var map2 = race.WinLossesOnMap.Single(m => m.Map == mapKey);
        return map2;
    }
}
