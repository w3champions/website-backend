using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace W3C.Contracts.Matchmaking
{
    public class GameMap
    {
        public string Sha1 { get; set; }
        public long Checksum { get; set; }
        public string Name { get; set; }
        public long Crc32 { get; set; }
        public string Description { get; set; }
        public string Author { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        [JsonProperty("suggested_players")]
        [JsonPropertyName("suggested_players")]
        public string SuggestedPlayers { get; set; }
        [JsonProperty("num_players")]
        [JsonPropertyName("num_players")]
        public int NumPlayers { get; set; }
        public GameMapForce[] Forces { get; set; } = new GameMapForce[0];
        public GameMapPlayer[] Players { get; set; } = new GameMapPlayer[0];
    }
}
