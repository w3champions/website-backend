using W3ChampionsStatisticService.PlayerStats;

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