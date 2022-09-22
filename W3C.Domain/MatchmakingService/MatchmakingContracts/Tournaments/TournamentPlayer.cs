using W3C.Domain.CommonValueObjects;

namespace W3C.Domain.MatchmakingService.MatchmakingContracts.Tournaments
{
    public class TournamentPlayer
    {
        public string BattleTag { get; set; }
        public GateWay Gateway { get; set; }
        public int? Seed { get; set; }
        public Race Race { get; set; }
        public bool Eliminated { get; set; }
    }
}
