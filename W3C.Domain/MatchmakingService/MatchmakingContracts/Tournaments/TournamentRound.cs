using System.Collections.Generic;

namespace W3C.Domain.MatchmakingService.MatchmakingContracts.Tournaments
{
    public class TournamentRound
    {
        public string Name { get; set; }
        public int Number { get; set; }
        public List<TournamentSeries> Series { get; set; }
    }
}
