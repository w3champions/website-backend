using System.Text.Json.Serialization;
using MongoDB.Bson;

namespace W3C.Domain.CommonValueObjects
{
    public class LoadingScreenTip
    {
        public string Message { get; set; }
        public string Author { get; set; }
        public string CreationDate { get; set; }
        [JsonIgnore]
        public ObjectId? Id { get; set; }
        public string BsonId => Id.ToString();
    }
}
