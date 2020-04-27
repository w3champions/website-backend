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
        public int RankingPointsProgress { get; set; }

        public void Update(bool won)
        {
            RecordWin(won);
        }

        public void Update(in int mmr, in int rankingPoints, in int rank, in int leagueId, in int leagueOrder)
        {
            MMR = mmr;
            RankingPoints = rankingPoints;
            Rank = rank;
            LeagueId = leagueId;
            LeagueOrder = leagueOrder;
        }
    }
}