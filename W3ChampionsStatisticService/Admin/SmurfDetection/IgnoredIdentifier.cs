using Newtonsoft.Json;

namespace W3ChampionsStatisticService.Admin.SmurfDetection;

public class IgnoredIdentifier
{
    // matchmaking-service serialises entities through BaseEntity.toJSON, which emits "_id" (the
    // "id" getter lives on the prototype and is not serialised). Outgoing JSON from this service
    // (System.Text.Json) still uses the property name "id".
    [JsonProperty("_id")]
    public string id { get; set; }
    public string type { get; set; }
    public string identifier { get; set; }
    public string author { get; set; }
    public string reason { get; set; }
}
