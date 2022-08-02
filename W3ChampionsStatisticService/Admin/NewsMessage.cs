using System.Text.Json.Serialization;
using MongoDB.Bson;

namespace W3ChampionsStatisticService.Admin
{
    public class NewsMessage
    {
        public string Message { get; set; }
        public string Date { get; set; }
        [JsonIgnore]
        public ObjectId? Id { get; set; }
        public string BsonId => Id.ToString();
    }
}