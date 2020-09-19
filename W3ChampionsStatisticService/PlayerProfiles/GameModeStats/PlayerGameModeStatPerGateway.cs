using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.PlayerProfiles.GameModeStats
{
    public class PlayerGameModeStatPerGateway : WinLoss, IIdentifiable
    {
        public static PlayerGameModeStatPerGateway Create(BattleTagIdCombined id)
        {
            return new PlayerGameModeStatPerGateway
            {
                Id = id.Id,
                Season = id.Season,
                GateWay = id.GateWay,
                GameMode = id.GameMode,
                PlayerIds = id.BattleTags,
                Race = id.Race
            };
        }

        public Race? Race { get; set; }

        public GameMode GameMode { get; set; }

        public GateWay GateWay { get; set; }

        public List<PlayerId> PlayerIds { get; set; }

        public int Season { get; set; }

        public string Id { get; set; }

        public int MMR { set; get; }
        public int RankingPoints { get; set; }
        public int Rank { get; set; }
        public int LeagueId { get; set; }
        public int LeagueOrder { get; set; }
        public int Division { get; set; }
        public float? Quantile { get; set; }

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
            if (RankProgressionStart == null || LastGameWasBefore8Hours())
            {
                RankProgressionStart = RankProgression.Create(mmr, rankingPoints);
            }

            MMR = mmr;
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