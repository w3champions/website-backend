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
