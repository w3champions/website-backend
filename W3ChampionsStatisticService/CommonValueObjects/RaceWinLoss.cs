namespace W3ChampionsStatisticService.CommonValueObjects
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