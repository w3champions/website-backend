using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace W3C.Contracts.Matchmaking;

public class GameMapForce
{
    public string Name { get; set; }
    public int Flags { get; set; }
    // Both attributes: Newtonsoft talks to matchmaking, System.Text.Json binds and answers the browser.
    [JsonProperty("player_set")]
    [JsonPropertyName("player_set")]
    public long PlayerSet { get; set; }

    // Responses used to spell the field `playerSet`, and a page loaded before that changed sends it
    // back that way, so the binder still accepts it. It is never written.
    [Newtonsoft.Json.JsonIgnore]
    [JsonPropertyName("playerSet")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? LegacyPlayerSet
    {
        get => null;
        set
        {
            if (value.HasValue) PlayerSet = value.Value;
        }
    }
}
