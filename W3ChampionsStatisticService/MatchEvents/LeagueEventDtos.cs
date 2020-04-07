using MongoDB.Bson.Serialization.Attributes;

namespace W3ChampionsStatisticService.MatchEvents
{
    public class LeagueConstellationChangedEvent
    {
        [BsonId]
        public int gateway { get; set; }
        public League[] leagues { get; set; }
    }

    public class League
    {
        public int id { get; set; }
        public string name { get; set; }
        public int order { get; set; }
        public int maxParticipantCount { get; set; }
    }

    public class RankingChangedEvent
    {
        public int gateway { get; set; }
        [BsonId]
        public int league { get; set; }
        public Rank[] ranks { get; set; }
    }

    public class Rank
    {
        public string id { get; set; }
        public double rp { get; set; }
    }
}