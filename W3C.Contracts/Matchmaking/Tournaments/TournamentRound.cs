using System.Collections.Generic;

namespace W3C.Contracts.Matchmaking.Tournaments
{
    public class TournamentRound
    {
        public string Name { get; set; }
        public int Number { get; set; }
        public List<TournamentSeries> Series { get; set; }
    }
}
