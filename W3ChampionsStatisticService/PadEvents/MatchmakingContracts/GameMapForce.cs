using Newtonsoft.Json;

namespace W3ChampionsStatisticService.PadEvents.MatchmakingContracts
{
    public class GameMapForce
    {
        public string Name { get; set; }
        public int Flags { get; set; }
        [JsonProperty("player_set")]
        public int PlayerSet { get; set; }
    }
}
