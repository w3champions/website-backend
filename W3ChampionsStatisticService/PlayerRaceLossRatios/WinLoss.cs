namespace W3ChampionsStatisticService.PlayerRaceLossRatios
{
    public class WinLoss
    {
        public void RecordWin(bool win)
        {
            if (win)
            {
                Wins++;
            }
            else
            {
                Losses++;
            }
        }
        public int Wins { get; set; }
        public int Losses { get; set; }
    }
}