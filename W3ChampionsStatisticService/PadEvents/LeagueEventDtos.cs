using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using W3ChampionsStatisticService.Matches;

namespace W3ChampionsStatisticService.PadEvents
{
    [BsonIgnoreExtraElements]
    public class LeagueConstellationChangedEvent
    {
        public int id { get; set; }
        public int gateway { get; set; }
        public GameMode gameMode { get; set; }
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
    [BsonNoId]
    public class RankingChangedEvent
    {
        [BsonElement("id")]
        public int id { get; set; }
        public int gateway { get; set; }
        public int league { get; set; }
        public GameMode gameMode { get; set; }
        public RankRaw[] ranks { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class RankRaw
    {
        public List<string> battleTags { get; set; }
        public double rp { get; set; }
    }
}