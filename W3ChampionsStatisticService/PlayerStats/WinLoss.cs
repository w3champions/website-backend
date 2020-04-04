namespace W3ChampionsStatisticService.PlayerStats
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
        public double Winrate => new WinRate(Wins, Losses).Rate;
    }
}