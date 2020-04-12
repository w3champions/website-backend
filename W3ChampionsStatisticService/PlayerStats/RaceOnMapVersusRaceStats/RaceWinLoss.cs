using W3ChampionsStatisticService.PlayerProfiles;

namespace W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats
{
    public class RaceWinLoss : WinLoss
    {
        public RaceWinLoss(Race race)
        {
            Race = race;
        }

        public Race Race { get; set; }
    }
}