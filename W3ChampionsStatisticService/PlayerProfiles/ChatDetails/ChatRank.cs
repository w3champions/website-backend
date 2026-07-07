using W3C.Contracts.Matchmaking;

namespace W3ChampionsStatisticService.PlayerProfiles.ChatDetails;

/// <summary>
/// Pinned chat-revamp contract §4 sub-payload of ChatDetailsDto (consumed by the chat-service
/// user directory for mention-autocomplete disambiguation): the player's BEST current-season
/// ladder rank across all gateways and game modes, including AT team ranks. Best = lowest
/// league Order (0 = highest league); ties broken by RankNumber, then GameMode, then GateWay —
/// deterministic for a given player and season. Serialized camelCase; GameMode/GateWay
/// serialize as their numeric enum values (System.Text.Json defaults, no converters).
/// </summary>
public class ChatRank(int leagueId, string leagueName, int leagueOrder, int leagueDivision, int rankNumber, GameMode gameMode, GateWay gateWay)
{
    /// <summary>League.Id within the season's LeagueConstellation for (season, gateWay, gameMode).</summary>
    public int LeagueId { get; } = leagueId;
    /// <summary>Display name, e.g. "Diamond".</summary>
    public string LeagueName { get; } = leagueName;
    /// <summary>0 = highest league; global ordering including divisions.</summary>
    public int LeagueOrder { get; } = leagueOrder;
    /// <summary>0 when the league has no divisions.</summary>
    public int LeagueDivision { get; } = leagueDivision;
    /// <summary>1-based position within the league/division.</summary>
    public int RankNumber { get; } = rankNumber;
    /// <summary>Mode the rank was earned in (numeric on the wire, e.g. GM_1v1 = 1).</summary>
    public GameMode GameMode { get; } = gameMode;
    /// <summary>Gateway the rank was earned on (numeric on the wire: America = 10, Europe = 20).</summary>
    public GateWay GateWay { get; } = gateWay;
}
