namespace W3C.Domain.MatchmakingService.MatchmakingContracts.Tournaments
{
    public class TournamentSeriesPlayer
    {
        public string BattleTag { get; set; }
        public int Team { get; set; }
        public int? Score { get; set; }
        public bool? Won { get; set; }
    }
}
