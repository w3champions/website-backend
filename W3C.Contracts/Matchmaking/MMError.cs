using Newtonsoft.Json;

namespace W3C.Contracts.Matchmaking;

/// <summary>
/// One entry of matchmaking-service's <c>errors</c> array (read with Newtonsoft, never re-served). express-validator
/// named the failing field <c>param</c> up to v6 and names it <c>path</c> from v7; both are read.
/// </summary>
public class MMError
{
    [JsonProperty("msg")]
    public string Message { get; set; }
    public string Param { get; set; }
    [JsonProperty("path")]
    public string Path { get; set; }
}
