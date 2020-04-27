using System;
using System.Text.Json.Serialization;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;

namespace W3ChampionsStatisticService.PlayerProfiles
{
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
        public RankProgression RankingPointsProgress
        {
            get
            {
                if (LastGameWasBefore8Hours()) return new RankProgression();
                return new RankProgression
                {
                    MMR = MMR - RankProgressionStart.MMR,
                    RankingPoints = RankingPoints - RankProgressionStart.RankingPoints,
                    LeagueId = LeagueId - RankProgressionStart.LeagueId,
                    LeagueOrder = LeagueOrder - RankProgressionStart.LeagueOrder,
                    Rank = Rank - RankProgressionStart.Rank
                };
            }
        }

        public void Update(bool won)
        {
            RecordWin(won);
        }

        public void Update(in int mmr, in int rankingPoints, in int rank, in int leagueId, in int leagueOrder)
        {
            MMR = mmr;
            if (RankProgressionStart == null || LastGameWasBefore8Hours())
            {
                RankProgressionStart = RankProgression.Create(mmr, rankingPoints, 0, leagueId, leagueOrder);
            }

            RankingPoints = rankingPoints;
            Rank = rank;
            LeagueId = leagueId;
            LeagueOrder = leagueOrder;
        }

        private bool LastGameWasBefore8Hours()
        {
            return RankProgressionStart.Date < DateTimeOffset.UtcNow - TimeSpan.FromHours(8);
        }

        [JsonIgnore]
        public RankProgression RankProgressionStart { get; set; }
    }

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
    }
}