using System.Text.Json.Serialization;

namespace W3ChampionsStatisticService.LagReports;

/// <summary>
/// Transport protocol used for the game stream. Serialized as the literal
/// "TCP" / "QUIC" string on the wire via <see cref="JsonStringEnumConverter"/>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Transport
{
    TCP,
    QUIC,
}

/// <summary>
/// System-derived connection-issue verdict tags attached to a lag report by the
/// launcher (NOT player-selected — kept distinct from <see cref="EIssueCategory"/>).
/// Serialized on the wire as the literal strings "LAN" / "LastMile" via
/// <see cref="JsonStringEnumListConverter{T}"/> (member names ARE the wire values).
/// </summary>
public enum ELagReportTag
{
    LAN,
    LastMile,
}

public enum EIssueCategory
{
    InputDelay,
    GameStutter,
    WaitingForPlayers,
    RubberBanding,
    SpikeLag,
    ConsistentLag,
    Reconnecting,
    FullDisconnect,
    Desync,
    FpsDrops,
    GameCrashed,
    Other,
}

public enum EConnectionEventType
{
    Reconnect,
    FailureDisconnect,
    GameCrashed,
    GamePaused,
    GameResumed,
    StartLag,
    StopLag,
}

public enum EConnectionType
{
    Direct,
    Proxied,
}

/// <summary>
/// Per-player leave reason as recorded by flo's observer-edge
/// (crates/observer-edge/src/game/mod.rs, PlayerLeaveReason).
///
/// IMPORTANT - <see cref="LeaveDisconnect"/> is AMBIGUOUS and must not be read as
/// "the player's connection dropped". flo's node fabricates it whenever a player is
/// removed without an explicit client-supplied reason
/// (crates/node/src/game/host/dispatch.rs, `reason.unwrap_or(LeaveReason::LeaveDisconnect)`):
/// a genuine client disconnect-leave, a lag-timeout drop, a majority drop vote, an
/// ack-queue overflow, an internal dispatch error and a desync eviction all land here.
/// Treat it as "was removed", nothing more.
///
/// Every one of these is ultimately CLIENT-ASSERTED: the reason is decoded verbatim
/// from the client's W3GS leave packet and never validated, so it is exactly as
/// forgeable as the scorescreen. Record it, never treat it as an outcome.
/// </summary>
public enum EFloLeaveReason
{
    LeaveDisconnect,
    LeaveLost,
    LeaveLostBuildings,
    LeaveWon,
    LeaveDraw,
    LeaveObserver,
    LeaveUnknown,
}

/// <summary>Which handler captured a <see cref="FloGameLeaveReport"/>.</summary>
public enum EFloLeaveCaptureTrigger
{
    Submission,
    MatchFinished,
    MatchCanceled,
}
