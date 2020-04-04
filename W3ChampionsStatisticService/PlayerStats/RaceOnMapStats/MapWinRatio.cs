using System.Collections.Generic;
using System.Linq;
using W3ChampionsStatisticService.PlayerProfiles;

namespace W3ChampionsStatisticService.PlayerStats.RaceOnMapStats
{
    public class MapWinRatio : List<MapWinLossRation>
    {
        public static MapWinRatio Create()
        {
            var ratio = new MapWinRatio
            {
                MapWinLossRation.Create(Race.RnD),
                MapWinLossRation.Create(Race.HU),
                MapWinLossRation.Create(Race.OC),
                MapWinLossRation.Create(Race.UD),
                MapWinLossRation.Create(Race.NE)

            };
            return ratio;
        }

        public void RecordWin(Race myRace, string map, bool won)
        {
            var mapWinLossRation = this.Single(r => r.Race == myRace);
            var singleOrDefault = mapWinLossRation.WinLosses.SingleOrDefault(r => r.Map == map);
            if (singleOrDefault == null)
            {
                mapWinLossRation.WinLosses.Add(new MapWinLoss(map));
            }

            mapWinLossRation.WinLosses.Single(r => r.Map == map).RecordWin(won);
        }
    }
}