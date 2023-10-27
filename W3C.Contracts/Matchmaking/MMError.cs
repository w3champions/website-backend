using Newtonsoft.Json;

namespace W3C.Contracts.Matchmaking;

public class MMError
{
    [JsonProperty("msg")]
    public string Message { get; set; }
    public string Param { get; set; }
}
