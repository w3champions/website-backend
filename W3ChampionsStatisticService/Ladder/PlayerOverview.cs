using W3ChampionsStatisticService.PlayerStats;

namespace W3ChampionsStatisticService.Ladder
{
    public class PlayerOverview
    {
        public PlayerOverview(string battleTag, int gateWay)
        {
            Id = battleTag;
            GateWay = gateWay;
            Name = battleTag.Split("#")[0];
            BattleTag = battleTag.Split("#")[1];
        }

        public string Id { get; set; }
        public string BattleTag { get; set; }
        public string Name { get; set; }
        public int TotalLosses { get; set; }
        public int TotalWins { get; set; }
        public int Games => TotalLosses + TotalWins;
        public double Winrate => new WinRate(TotalWins, TotalLosses).Rate;
        public int MMR { get; set; }
        public int GateWay { get; set; }

        public void RecordWin(bool won, int newMmr)
        {
            MMR = newMmr;
            if (won)
            {
                TotalWins++;
            }
            else
            {
                TotalLosses++;
            }
        }
    }
}