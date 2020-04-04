using System.Linq;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.PlayerStats;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapStats;
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
    }
}