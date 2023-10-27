using Newtonsoft.Json;

namespace W3C.Contracts.Matchmaking;

public class GameMapForce
{
    public string Name { get; set; }
    public int Flags { get; set; }
    [JsonProperty("player_set")]
    public long PlayerSet { get; set; }
}
