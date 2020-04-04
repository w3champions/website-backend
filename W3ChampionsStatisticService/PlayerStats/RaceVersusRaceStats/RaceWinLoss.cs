using W3ChampionsStatisticService.PlayerProfiles;

namespace W3ChampionsStatisticService.PlayerStats.RaceVersusRaceStats
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