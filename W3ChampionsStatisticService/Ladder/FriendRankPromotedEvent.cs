using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;

namespace W3ChampionsStatisticService.Ladder;

/// <summary>Pushed over <see cref="Hubs.WebsiteBackendHub"/> to online friends of a player
/// whose single-member ladder standing moved into a higher league. Additive message: clients
/// that do not subscribe to it are unaffected.</summary>
public class FriendRankPromotedEvent
{
    public string BattleTag { get; set; }
    public int Season { get; set; }
    public GateWay Gateway { get; set; }
    public GameMode GameMode { get; set; }
    public Race? Race { get; set; }
    public string LeagueName { get; set; }
    public int LeagueOrder { get; set; }
    public int LeagueDivision { get; set; }
    public string OldLeagueName { get; set; }
    public int OldLeagueOrder { get; set; }
    public int OldLeagueDivision { get; set; }
}
