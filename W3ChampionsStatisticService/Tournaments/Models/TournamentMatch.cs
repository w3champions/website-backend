using System.Collections.Generic;

namespace W3ChampionsStatisticService.Tournaments.Models
{
    public class TournamentMatch
    {
        public string Id { get; set; }
        public List<TournamentPlayer> Players { get; set; }
    }
}
