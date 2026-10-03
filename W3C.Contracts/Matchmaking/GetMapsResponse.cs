using Newtonsoft.Json;

namespace W3C.Contracts.Matchmaking;

/// <summary>
/// A map listing from matchmaking. <c>items</c> is required when read from matchmaking: a listing without it is a
/// contract violation (see W3C.Domain's UpstreamContract), never an empty listing.
/// </summary>
public class GetMapsResponse
{
    public int Total { get; set; }

    [JsonProperty(Required = Required.Always)]
    public MapContract[] Items { get; set; }
}
