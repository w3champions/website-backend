using Newtonsoft.Json;

namespace W3ChampionsStatisticService.PadEvents.MatchmakingContracts
{
    public class GameMap
    {
        public string Path { get; set; }
        public string Sha1 { get; set; }
        public int Checksum { get; set; }
        public string Name { get; set; }
        public int Crc32 { get; set; }
        public string Description { get; set; }
        public string Author { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        [JsonProperty("suggested_players")]
        public string SuggestedPlayers { get; set; }
        [JsonProperty("num_players")]
        public int NumPlayers { get; set; }
        public GameMapForce[] Forces { get; set; } = new GameMapForce[0];
        public GameMapPlayer[] Players { get; set; } = new GameMapPlayer[0];
    }
}
