using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace W3C.Contracts.Matchmaking.Tournaments;

public class TournamentMatch
{
    [JsonIgnore]
    public string _Id { get; set; }
    public string Id => _Id.ToString();
    public TournamentMatchState State { get; set; }
    public int MapId { get; set; }
    public List<TournamentMatchPlayer> players { get; set; }
}
