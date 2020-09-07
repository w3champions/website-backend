using System.Collections.Generic;

namespace W3ChampionsStatisticService.Tournaments.Models
{
    public class TournamentRound
    {
        public string Name { get; set; }
        public int Round { get; set; }
        public List<TournamentMatch> Matches { get; set; }
        public RoundsConnectionType ConnectionType { get; set; }
    }
}
