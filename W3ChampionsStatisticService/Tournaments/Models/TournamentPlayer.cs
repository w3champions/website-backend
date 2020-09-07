using W3ChampionsStatisticService.CommonValueObjects;

namespace W3ChampionsStatisticService.Tournaments.Models
{
    public class TournamentPlayer
    {
        public string Name { get; set; }
        public Race Race { get; set; }
        public string CountryCode { get; set; }
        public int? Score { get; set; }
    }
}
