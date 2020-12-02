using W3ChampionsStatisticService.CommonValueObjects;

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
        public string Location { get; set; }
        public string CountryCode { get; set; }
        public string Country { get; set; }
        public string Twitch { get; set; }
    }
}