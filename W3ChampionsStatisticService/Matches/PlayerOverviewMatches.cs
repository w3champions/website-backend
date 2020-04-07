using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.PlayerStats;

namespace W3ChampionsStatisticService.Matches
{
    public class PlayerOverviewMatches
    {
        public int Wins { get; set; }
        public int Losses { get; set; }
        public Race Race { get; set; }
        public int OldMmr { get; set; }
        public int CurrentMmr { get; set; }
        public string BattleTag { get; set; }
        public string Name { get; set; }
        public int MmrGain => CurrentMmr - OldMmr;
        public double Winrate => new WinRate(Wins, Losses).Rate;
        public double Games => Wins + Losses;
        public bool Won { get; set; }
    }
}