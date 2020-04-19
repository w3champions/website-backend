using MongoDB.Bson.Serialization.Attributes;

namespace W3ChampionsStatisticService.PadEvents
{
    [BsonIgnoreExtraElements]
    public class LeagueConstellationChangedEvent
    {
        public int gateway { get; set; }
        public League[] leagues { get; set; }
    }

    [BsonIgnoreExtraElements]
    [BsonNoId]
    public class League
    {
        [BsonElement("id")]
        public int id { get; set; }
        public string name { get; set; }
        public int order { get; set; }
        public int maxParticipantCount { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class RankingChangedEvent
    {
        public int gateway { get; set; }
        public int league { get; set; }
        public RankRaw[] ranks { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class RankRaw
    {
        public string tagId { get; set; }
        public double rp { get; set; }
    }
}