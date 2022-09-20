using System.Collections.Generic;

namespace W3C.Domain.MatchmakingService.MatchmakingContracts.Tournaments
{
    public class TournamentMatch
    {
        public string Id { get; set; }
        public int MapId { get; set; }
        public List<TournamentMatchPlayer> players { get; set; }
    }
}
