using Newtonsoft.Json;

namespace W3C.Domain.MatchmakingService.MatchmakingContracts
{
    public class GameMapForce
    {
        public string Name { get; set; }
        public int Flags { get; set; }
        [JsonProperty("player_set")]
        public long PlayerSet { get; set; }
    }
}
