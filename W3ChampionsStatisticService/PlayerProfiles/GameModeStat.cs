using System;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using W3ChampionsStatisticService.CommonValueObjects;

namespace W3ChampionsStatisticService.PlayerProfiles
{
    [BsonIgnoreExtraElements]
    public class GameModeStat : WinLoss
    {
        public GameModeStat(GameMode gameMode)
        {
            Mode = gameMode;
        }

        public GameMode Mode { set; get; }
        public int MMR { set; get; }
        public int RankingPoints { get; set; }
        public int Rank { get; set; }
        public int LeagueId { get; set; }
        public int LeagueOrder { get; set; }
        public int Division { get; set; }

        public RankProgression RankingPointsProgress
        {
            get
            {
                if (LastGameWasBefore8Hours()) return new RankProgression();
                return new RankProgression  {
                    MMR = MMR - RankProgressionStart.MMR,
                    RankingPoints = RankingPoints - RankProgressionStart.RankingPoints,
                };
            }
        }

        public void RecordRanking(in int mmr, in int rankingPoints)
        {
            MMR = mmr;
            if (RankProgressionStart == null || LastGameWasBefore8Hours())
            {
                RankProgressionStart = RankProgression.Create(mmr, rankingPoints);
            }

            RankingPoints = rankingPoints;
        }

        private bool LastGameWasBefore8Hours()
        {
            if (RankProgressionStart == null) return true;
            return RankProgressionStart.Date < DateTimeOffset.UtcNow - TimeSpan.FromHours(8);
        }

        [JsonIgnore]
        public RankProgression RankProgressionStart { get; set; }
    }
}