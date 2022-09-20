using System.Collections.Generic;

namespace W3C.Domain.MatchmakingService.MatchmakingContracts.Tournaments
{
    public class TournamentSeries
    {
        public string Id { get; set; }
        public List<TournamentSeriesPlayer> Players { get; set; }
        public List<TournamentMatch> Matches { get; set; }
    }
}
