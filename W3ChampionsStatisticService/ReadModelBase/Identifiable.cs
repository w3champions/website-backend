using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace W3ChampionsStatisticService.ReadModelBase
{
    public abstract class Identifiable
    {
        [BsonId]
        public ObjectId Id { get; set; }
    }
}