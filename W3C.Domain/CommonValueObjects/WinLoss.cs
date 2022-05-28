namespace W3C.Domain.CommonValueObjects
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
        public int Games => Wins + Losses;
        public double Winrate => new WinRate(Wins, Losses).Rate;
    }
}