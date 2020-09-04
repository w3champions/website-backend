using System.Collections.Generic;
using System.Text.Json.Serialization;
using MongoDB.Bson;

namespace W3ChampionsStatisticService.Tournaments.Models
{
    public class Tournament
    {
        [JsonIgnore]
        public ObjectId Id { get; set; }

        [JsonPropertyName("id")]
        public string ObjectId => Id.ToString();

        public string Name { get; set; }

        public List<TournamentRound> WinnerBracketRounds { get; set; }
        public List<TournamentRound> LoserBracketRounds { get; set; }
    }
}
