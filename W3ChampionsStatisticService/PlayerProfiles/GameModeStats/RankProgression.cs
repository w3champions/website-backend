using System;
using System.Text.Json.Serialization;

namespace W3ChampionsStatisticService.PlayerProfiles.GameModeStats
{
    public class RankProgression
    {
        public static RankProgression Create(in int mmr, in double rankingPoints)
        {
            return new RankProgression
            {
                Date = DateTimeOffset.UtcNow,
                RankingPoints = rankingPoints,
                MMR = mmr,
            };
        }

        [JsonIgnore]
        public DateTimeOffset Date { get; set; }
        public double RankingPoints { get; set; }
        public double MMR { get; set; }
    }
}