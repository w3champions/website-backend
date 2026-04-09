using System;
using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using W3C.Domain.Repositories;

namespace W3ChampionsStatisticService.LagReports;

/// <summary>
/// Root document — one per game. Multiple players' reports are merged
/// into the same document via UpsertPlayerData.
/// </summary>
[BsonIgnoreExtraElements]
public class LagReport : IIdentifiable
{
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>W3C match ID (from matchmaking).</summary>
    public int GameId { get; set; }

    /// <summary>Flo game ID (from flo-controller).</summary>
    public int FloGameId { get; set; }

    public string GameName { get; set; }
    public string MapPath { get; set; }

    /// <summary>The flo-node that hosted this game.</summary>
    public int ServerNodeId { get; set; }
    public string ServerNodeName { get; set; }

    /// <summary>True if at least one player explicitly submitted (clicked Submit).</summary>
    public bool HasExplicitReport { get; set; }

    public List<LagReportPlayer> Players { get; set; } = [];

    /// <summary>
    /// Server-side ping data fetched from flo-stats-service after the game ends.
    /// Null until the match-finished handler populates it.
    /// </summary>
    public List<ServerSidePingData> ServerSidePing { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Per-player data within a lag report.
/// Each player in the game can submit independently (explicit or auto).
/// </summary>
public class LagReportPlayer
{
    public string BattleTag { get; set; }

    /// <summary>Player's public IP at time of game (self-reported by launcher).</summary>
    public string ClientIp { get; set; }

    /// <summary>Whether the player connected directly or via a proxy node.</summary>
    public EConnectionType ConnectionType { get; set; }

    /// <summary>Name of the proxy node (null if direct).</summary>
    public string ProxyName { get; set; }

    /// <summary>IP of the proxy node (null if direct). Split from proxy_address for filtering.</summary>
    public string ProxyIp { get; set; }

    /// <summary>Port of the proxy node (null if direct).</summary>
    public int? ProxyPort { get; set; }

    /// <summary>True if the player explicitly clicked Submit (vs auto-submit on dismiss/close).</summary>
    public bool IsExplicit { get; set; }

    /// <summary>Issue categories selected by the player (e.g. SpikeLag, Reconnecting).</summary>
    public List<EIssueCategory> IssueCategories { get; set; } = [];

    /// <summary>Free-text description from the player.</summary>
    public string FreeText { get; set; } = "";

    /// <summary>Per-lag-event annotations added by the player in the post-game dialog.</summary>
    public List<LagReportAnnotation> Annotations { get; set; } = [];

    /// <summary>All diagnostics data collected by the flo client during the game.</summary>
    public PlayerDiagnostics Diagnostics { get; set; }
}

/// <summary>
/// Diagnostics measurements collected by the flo client for a single player.
/// </summary>
public class PlayerDiagnostics
{
    /// <summary>Lag events triggered by the player via !lag command.</summary>
    public List<LagEvent> LagEvents { get; set; } = [];

    /// <summary>MTR traces to the target/proxy: 60s continuous + 5s burst on events.</summary>
    public List<TraceMeasurement> TargetMtr { get; set; } = [];

    /// <summary>Baseline MTR to all servers at game start/end and on events.</summary>
    public List<ServerBaseline> AllServerBaselines { get; set; } = [];

    /// <summary>Reverse MTR from flo-node back to the player.</summary>
    public List<TraceMeasurement> ReverseMtr { get; set; } = [];

    /// <summary>Periodic ping snapshots (min/max/avg/stddev/loss) from the PingActor.</summary>
    public List<PingSample> PingHistory { get; set; } = [];

    /// <summary>Connection events: reconnects and disconnects.</summary>
    public List<ConnectionEventData> ConnectionEvents { get; set; } = [];
}

/// <summary>A !lag event: the moment the player entered the command.</summary>
public class LagEvent
{
    [BsonRepresentation(BsonType.Array)]
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>Milliseconds since game start.</summary>
    public long GameTimeOffsetMs { get; set; }

    /// <summary>Player annotation added in post-game dialog (null if not annotated).</summary>
    public string Annotation { get; set; }
}

/// <summary>A single MTR trace (forward to target or reverse from flo-node).</summary>
public class TraceMeasurement
{
    [BsonRepresentation(BsonType.Array)]
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>IP address of the trace target.</summary>
    public string Target { get; set; }

    public List<HopData> Hops { get; set; } = [];
}

/// <summary>Baseline MTR trace to a specific game server.</summary>
public class ServerBaseline
{
    [BsonRepresentation(BsonType.Array)]
    public DateTimeOffset Timestamp { get; set; }

    public int ServerId { get; set; }
    public string ServerName { get; set; }

    /// <summary>IP address of the server.</summary>
    public string Target { get; set; }

    public List<HopData> Hops { get; set; } = [];
}

/// <summary>A single hop in an MTR trace.</summary>
public class HopData
{
    public int HopNumber { get; set; }

    /// <summary>IP address of the hop (null if no response / timeout).</summary>
    public string Host { get; set; }

    public double? AvgRttMs { get; set; }
    public double? MinRttMs { get; set; }
    public double? MaxRttMs { get; set; }
    public double? StddevMs { get; set; }

    /// <summary>Packet loss percentage (0–100).</summary>
    public double LossPercent { get; set; }
}

/// <summary>A periodic ping snapshot from the flo client's PingActor.</summary>
public class PingSample
{
    [BsonRepresentation(BsonType.Array)]
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>Ping values in milliseconds.</summary>
    public int? Min { get; set; }
    public int? Max { get; set; }
    public int? Avg { get; set; }
    public float? Stddev { get; set; }
    public int? Current { get; set; }

    /// <summary>Packet loss rate (0.0–1.0).</summary>
    public float LossRate { get; set; }
}

/// <summary>A connection event: reconnect or disconnect.</summary>
public class ConnectionEventData
{
    [BsonRepresentation(BsonType.Array)]
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>Milliseconds since game start. Frozen during disconnects (no ticks arrive).</summary>
    public long GameTimeOffsetMs { get; set; }

    public EConnectionEventType EventType { get; set; }

    /// <summary>For reconnects: how long the disconnection lasted (ms). Null for disconnects.</summary>
    public long? DurationMs { get; set; }
}

/// <summary>Player annotation for a specific lag event, keyed by game time offset.</summary>
public class LagReportAnnotation
{
    /// <summary>Milliseconds since game start — matches the LagEvent it annotates.</summary>
    public long GameTimeOffsetMs { get; set; }

    public string Text { get; set; }
}

/// <summary>
/// Server-side ping data for a player, fetched from flo-stats-service GraphQL
/// after the game ends. Provides the server's view of ping (complements client-side PingSample).
/// </summary>
public class ServerSidePingData
{
    public int PlayerId { get; set; }
    public string PlayerName { get; set; }
    public List<ServerPingSample> Samples { get; set; } = [];
}

/// <summary>A single server-side ping snapshot from flo-stats.</summary>
public class ServerPingSample
{
    /// <summary>Seconds since game start.</summary>
    public double Time { get; set; }
    public int? Min { get; set; }
    public int? Max { get; set; }
    public int? Avg { get; set; }
}
