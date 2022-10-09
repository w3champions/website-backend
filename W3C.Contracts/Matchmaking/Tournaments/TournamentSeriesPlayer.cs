namespace W3C.Contracts.Matchmaking.Tournaments
{
    public class TournamentSeriesPlayer
    {
        public string BattleTag { get; set; }
        public int Team { get; set; }
        public int? Score { get; set; }
        public bool? Won { get; set; }
    }
}
