using System.Linq;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.PlayerStats;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapStats;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;
using W3ChampionsStatisticService.PlayerStats.RaceVersusRaceStats;

namespace WC3ChampionsStatisticService.UnitTests
{
    public static class RaceExtensions
    {
        public static WinLoss GetWinLoss(this RaceVersusRaceRatio ratio, Race myRace, Race enemyRace)
        {
            return ratio.RaceWinRatio.Single(r => r.Race == myRace).WinLosses.Single(r => r.Race == enemyRace);
        }

        public static WinLoss GetWinLoss(this RaceOnMapRatio ratio, Race myRace, string map)
        {
            return ratio.RaceWinRatio.Single(r => r.Race == myRace).WinLosses.Single(r => r.Map == map);
        }

        public static WinLoss GetWinLoss(this RaceOnMapVersusRaceRatio ratio, Race myRace, Race enemyRace, string map)
        {
            return ratio.RaceWinsOnMap
                .Single(r => r.Race == myRace).WinLossesOnMap
                .Single(r => r.Map == map).WinLosses
                .Single(w => w.Race == enemyRace);
        }
    }
}