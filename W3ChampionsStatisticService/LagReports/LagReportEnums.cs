namespace W3ChampionsStatisticService.LagReports;

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
    Other,
}

public enum EConnectionEventType
{
    Reconnect,
    FailureDisconnect,
}

public enum EConnectionType
{
    Direct,
    Proxied,
}
