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
        public int Division => CalculateDivision(LeagueId);

        private int CalculateDivision(int leagueId)
        {
            if (leagueId == 0 || leagueId == 1) return 0;
            return (leagueId - 2) % 6;
        }

        public int LeagueOrder { get; set; }
        public RankProgression RankingPointsProgress
        {
            get
            {
                if (LastGameWasBefore8Hours()) return new RankProgression();
                return new RankProgression  {
                    MMR = MMR - RankProgressionStart.MMR,
                    RankingPoints = RankingPoints - RankProgressionStart.RankingPoints,
                    LeagueId = LeagueId - RankProgressionStart.LeagueId,
                    LeagueOrder = LeagueOrder - RankProgressionStart.LeagueOrder,
                    Rank = RankProgressionStart.Rank - Rank,
                    Division = CalculateDivision(LeagueId) - CalculateDivision(RankProgressionStart.LeagueId)
                };
            }
        }

        public void Update(bool won)
        {
            RecordWin(won);
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