using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace W3ChampionsStatisticService.MongoDb
{
    public abstract class Identifiable
    {
        [BsonId]
        public ObjectId Id { get; set; }
    }
}