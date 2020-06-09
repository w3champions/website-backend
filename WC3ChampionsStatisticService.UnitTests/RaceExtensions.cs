using System.Linq;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;

namespace WC3ChampionsStatisticService.UnitTests
{
    public static class RaceExtensions
    {
        public static WinLoss GetWinLoss(this PlayerRaceOnMapVersusRaceRatio ratio, Race myRace, Race enemyRace, string map, string patch)
        {
            return ratio.RaceWinsOnMapByPatch[patch]
                .Single(r => r.Race == myRace).WinLossesOnMap
                .Single(r => r.Map == map).WinLosses
                .Single(w => w.Race == enemyRace);
        }
    }
}