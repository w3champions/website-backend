using W3ChampionsStatisticService.PlayerStats;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;

namespace W3ChampionsStatisticService.PlayerProfiles
{
    public class RaceStat : WinLoss
    {
        public RaceStat(Race race)
        {
            Race = race;
        }

        public Race Race { set; get; }
    }
}