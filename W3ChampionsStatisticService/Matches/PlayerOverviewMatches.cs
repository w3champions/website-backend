namespace W3ChampionsStatisticService.Matches
{
    public class PlayerOverviewMatches
    {
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int OldMmr { get; set; }
        public int CurrentMmr { get; set; }
        public string BattleTag { get; set; }
        public string Name { get; set; }
        public int MmrGain => CurrentMmr - OldMmr;
        public double Winrate => Wins / (double)(Wins + Losses);
    }
}