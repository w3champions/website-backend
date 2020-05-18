using W3ChampionsStatisticService.CommonValueObjects;

namespace W3ChampionsStatisticService.PlayerProfiles
{
    public class BaseRankedStat : WinLoss
    {
        public int MMR { set; get; }
        public int RankingPoints { get; set; }
        public int Rank { get; set; }
        public int LeagueId { get; set; }
        public int LeagueOrder { get; set; }
        public int Division { get; set; }
    }
}