using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace W3C.Domain.MatchmakingService.MatchmakingContracts.Tournaments
{
    public class TournamentSeries
    {
        [JsonIgnore]
        public string _Id { get; set; }
        public string Id => _Id.ToString();
        public List<TournamentSeriesPlayer> Players { get; set; }
        public List<TournamentMatch> Matches { get; set; }
        public TournamentSeriesState State { get; set; }
    }
}
