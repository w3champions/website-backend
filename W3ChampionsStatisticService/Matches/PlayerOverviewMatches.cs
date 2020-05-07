using W3ChampionsStatisticService.PlayerProfiles;

namespace W3ChampionsStatisticService.Matches
{
    public class PlayerOverviewMatches
    {
        public Race Race { get; set; }
        public int OldMmr { get; set; }
        public int CurrentMmr { get; set; }
        public string BattleTag { get; set; }
        public string Name { get; set; }
        public int MmrGain => CurrentMmr - OldMmr;
        public bool Won { get; set; }
    }
}