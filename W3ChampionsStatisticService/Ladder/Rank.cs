using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Ladder
{
    public class Rank : IIdentifiable
    {
        public Rank(int gateway, int league, int rankNumber, int rankingPoints, string playerId)
        {
            Gateway = gateway;
            League = league;
            RankNumber = rankNumber;
            RankingPoints = rankingPoints;
            PlayerId = playerId;
        }

        public int Gateway { get; set; }
        public string Id => $"{RankNumber}@{League}@{Gateway}";
        public int League { get; set; }
        public int RankNumber { get; set; }
        public int RankingPoints { get; set; }
        public string PlayerId { get; set; }
    }
}