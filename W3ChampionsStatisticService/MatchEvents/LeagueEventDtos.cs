namespace W3ChampionsStatisticService.MatchEvents
{
    public class LeagueConstellationChangedEvent
    {
        public int id => gateway;
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
        public string id => $"{league}@{gateway}";
        public int league { get; set; }
        public RankRaw[] ranks { get; set; }
    }

    public class RankRaw
    {
        public string id { get; set; }
        public double rp { get; set; }
    }
}