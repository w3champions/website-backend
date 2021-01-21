using W3ChampionsStatisticService.CommonValueObjects;

namespace W3ChampionsStatisticService.Tournaments.Models
{
    public class TournamentPlayer
    {
        // remove this prop when finished ui for it
        public string Name { get; set; }
        public string BattleTag { get; set; }
        public Race Race { get; set; }
        public string CountryCode { get; set; }
        public int? Score { get; set; }
    }
}
