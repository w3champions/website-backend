using W3C.Contracts.GameObjects;

namespace W3C.Contracts.Matchmaking.Tournaments;

public class TournamentPlayer
{
    public string BattleTag { get; set; }
    public GateWay Gateway { get; set; }
    public int? Seed { get; set; }
    public Race Race { get; set; }
    public bool Eliminated { get; set; }
    public string CountryCode { get; set; }
}
