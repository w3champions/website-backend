using System;
using System.Text.Json.Serialization;

namespace W3ChampionsStatisticService.PlayerProfiles
{
    public class RankProgression
    {
        public static RankProgression Create(in int mmr, in int rankingPoints, int rank, in int leagueId,
            in int leagueOrder)
        {
            return new RankProgression
            {
                Date = DateTimeOffset.UtcNow,
                RankingPoints = rankingPoints,
                MMR = mmr,
                LeagueId = leagueId,
                LeagueOrder = leagueOrder,
                Rank = rank
            };
        }

        public int Rank { get; set; }

        [JsonIgnore]
        public DateTimeOffset Date { get; set; }
        public double RankingPoints { get; set; }
        public double MMR { get; set; }
        public int LeagueId { get; set; }
        public int LeagueOrder { get; set; }
        public int Division { get; set; }
    }
}