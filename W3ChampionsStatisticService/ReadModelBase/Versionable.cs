using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace W3ChampionsStatisticService.ReadModelBase
{
    public abstract class Versionable
    {
        [BsonId]
        public ObjectId Id { get; set; }
    }
}