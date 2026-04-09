using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace W3ChampionsStatisticService.LagReports;

// ── Submission DTOs (match launcher snake_case JSON) ──────────────────

public class LagReportSubmissionDto
{
    [JsonPropertyName("diagnostics")]
    public DiagnosticsDataDto Diagnostics { get; set; }

    [JsonPropertyName("game_metadata")]
    public GameMetadataDto GameMetadata { get; set; }

    [JsonPropertyName("connection_topology")]
    public ConnectionTopologyDto ConnectionTopology { get; set; }

    [JsonPropertyName("is_explicit")]
    public bool IsExplicit { get; set; }

    [JsonPropertyName("annotations")]
    public List<AnnotationDto> Annotations { get; set; } = [];

    [JsonPropertyName("categories")]
    [JsonConverter(typeof(JsonStringEnumListConverter<EIssueCategory>))]
    public List<EIssueCategory> Categories { get; set; } = [];

    [JsonPropertyName("free_text")]
    public string FreeText { get; set; } = "";
}

public class GameMetadataDto
{
    [JsonPropertyName("game_id")]
    public int GameId { get; set; }

    [JsonPropertyName("flo_game_id")]
    public int FloGameId { get; set; }

    [JsonPropertyName("map_path")]
    public string MapPath { get; set; } = "";

    [JsonPropertyName("game_name")]
    public string GameName { get; set; } = "";
}

public class ConnectionTopologyDto
{
    [JsonPropertyName("server_node_id")]
    public int ServerNodeId { get; set; }

    [JsonPropertyName("server_node_name")]
    public string ServerNodeName { get; set; } = "";

    [JsonPropertyName("proxy_name")]
    public string ProxyName { get; set; }

    [JsonPropertyName("proxy_address")]
    public string ProxyAddress { get; set; }

    [JsonPropertyName("connection_type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EConnectionType ConnectionType { get; set; } = EConnectionType.Direct;

    [JsonPropertyName("client_ip")]
    public string ClientIp { get; set; }
}

public class DiagnosticsDataDto
{
    [JsonPropertyName("game_id")]
    public int GameId { get; set; }

    [JsonPropertyName("player_id")]
    public int PlayerId { get; set; }

    [JsonPropertyName("lag_events")]
    public List<LagEventDto> LagEvents { get; set; } = [];

    [JsonPropertyName("target_mtr")]
    public List<TimedTraceDto> TargetMtr { get; set; } = [];

    [JsonPropertyName("all_server_baselines")]
    public List<ServerTraceDto> AllServerBaselines { get; set; } = [];

    [JsonPropertyName("reverse_mtr")]
    public List<TimedTraceDto> ReverseMtr { get; set; } = [];

    [JsonPropertyName("ping_history")]
    public List<TimedPingStatsDto> PingHistory { get; set; } = [];

    [JsonPropertyName("connection_events")]
    public List<ConnectionEventDto> ConnectionEvents { get; set; } = [];
}

public class LagEventDto
{
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonPropertyName("game_time_offset")]
    public long GameTimeOffsetMs { get; set; }

    [JsonPropertyName("annotation")]
    public string Annotation { get; set; }
}

public class TimedTraceDto
{
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonPropertyName("trace")]
    public TraceResultDto Trace { get; set; }
}

public class ServerTraceDto
{
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonPropertyName("server_id")]
    public int ServerId { get; set; }

    [JsonPropertyName("server_name")]
    public string ServerName { get; set; }

    [JsonPropertyName("trace")]
    public TraceResultDto Trace { get; set; }
}

public class TraceResultDto
{
    [JsonPropertyName("target")]
    public string Target { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonPropertyName("hops")]
    public List<HopDto> Hops { get; set; } = [];
}

public class HopDto
{
    [JsonPropertyName("hop_number")]
    public int HopNumber { get; set; }

    [JsonPropertyName("host")]
    public string Host { get; set; }

    [JsonPropertyName("avg_rtt_ms")]
    public double? AvgRttMs { get; set; }

    [JsonPropertyName("min_rtt_ms")]
    public double? MinRttMs { get; set; }

    [JsonPropertyName("max_rtt_ms")]
    public double? MaxRttMs { get; set; }

    [JsonPropertyName("stddev_ms")]
    public double? StddevMs { get; set; }

    [JsonPropertyName("loss_percent")]
    public double LossPercent { get; set; }
}

public class TimedPingStatsDto
{
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonPropertyName("stats")]
    public PingStatsDto Stats { get; set; }
}

public class PingStatsDto
{
    [JsonPropertyName("min")]
    public double? Min { get; set; }

    [JsonPropertyName("max")]
    public double? Max { get; set; }

    [JsonPropertyName("avg")]
    public double? Avg { get; set; }

    [JsonPropertyName("stddev")]
    public double? Stddev { get; set; }

    [JsonPropertyName("current")]
    public double? Current { get; set; }

    [JsonPropertyName("loss_rate")]
    public double LossRate { get; set; }
}

public class ConnectionEventDto
{
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonPropertyName("game_time_offset")]
    public long GameTimeOffsetMs { get; set; }

    [JsonPropertyName("event_type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EConnectionEventType EventType { get; set; }

    [JsonPropertyName("duration")]
    public long? DurationMs { get; set; }
}

public class AnnotationDto
{
    [JsonPropertyName("game_time_offset")]
    public long GameTimeOffsetMs { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; }
}

// ── Response ──────────────────────────────────────────────────────────

public class LagReportSubmissionResponse
{
    [JsonPropertyName("report_id")]
    public string ReportId { get; set; }
}

// ── Admin query request ───────────────────────────────────────────────

public class LagReportQueryRequest
{
    public string BattleTag { get; set; }
    public string GameSearch { get; set; }
    public string ServerName { get; set; }
    public string ProxyName { get; set; }
    public string ProxyIp { get; set; }
    public string DateFrom { get; set; }
    public string DateTo { get; set; }
    public string IssueCategory { get; set; }
    public bool? ExplicitOnly { get; set; }
    public int Page { get; set; } = 0;
    public int PageSize { get; set; } = 20;
}

// ── Admin list item ───────────────────────────────────────────────────

public class LagReportListItem
{
    public string Id { get; set; }
    public int GameId { get; set; }
    public int FloGameId { get; set; }
    public string GameName { get; set; }
    public string MapPath { get; set; }
    public int ServerNodeId { get; set; }
    public string ServerNodeName { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool HasExplicitReport { get; set; }
    public List<LagReportPlayerSummary> Players { get; set; } = [];
}

public class LagReportPlayerSummary
{
    public string BattleTag { get; set; }
    public bool IsExplicit { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EConnectionType ConnectionType { get; set; }
    public string ProxyName { get; set; }
    [JsonConverter(typeof(JsonStringEnumListConverter<EIssueCategory>))]
    public List<EIssueCategory> IssueCategories { get; set; } = [];
    public int LagEventCount { get; set; }
    public int ConnectionEventCount { get; set; }
}
