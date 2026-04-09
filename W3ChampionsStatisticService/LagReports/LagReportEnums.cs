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
