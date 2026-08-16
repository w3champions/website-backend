using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
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

    [JsonPropertyName("connection_issue_tags")]
    [JsonConverter(typeof(JsonStringEnumListConverter<ELagReportTag>))]
    public List<ELagReportTag> ConnectionIssueTags { get; set; } = [];

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

    /// <summary>
    /// Transport protocol used for the game stream (TCP or QUIC).
    /// Defaults to TCP so older launchers that don't send this field stay valid.
    /// </summary>
    [JsonPropertyName("transport")]
    [EnumDataType(typeof(Transport), ErrorMessage = "Transport must be TCP or QUIC.")]
    public Transport Transport { get; set; } = Transport.TCP;
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

    /// <summary>Server-name prefixes, OR'd together — repeat the query param to send several.
    /// A single value binds the same way, so existing callers are unaffected.</summary>
    public List<string> ServerName { get; set; }

    /// <summary>Exact node ids, OR'd together — repeat the query param to send several.</summary>
    public List<int> ServerNodeId { get; set; }

    public string ProxyName { get; set; }
    public string ProxyIp { get; set; }
    public string DateFrom { get; set; }
    public string DateTo { get; set; }

    /// <summary>Issue categories, OR'd together: a report matches when any player carries any
    /// of them — players self-report one incident inconsistently, and OR sees it whole.</summary>
    public List<string> IssueCategory { get; set; }

    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "connection_issue_tag")]
    public List<string> ConnectionIssueTag { get; set; }

    public bool? ExplicitOnly { get; set; }

    /// <summary>Bounds on the game's player count (the materialized PlayerCount field).
    /// Null or non-positive means unbounded on that end.</summary>
    public int? MinPlayers { get; set; }
    public int? MaxPlayers { get; set; }

    public int Page { get; set; } = 0;
    public int PageSize { get; set; } = 20;
}

public static class LagReportQueryValidation
{
    /// <summary>
    /// First problem with the request's filter values, or null. Unknown values turn into a
    /// 400 rather than silently matching nothing (or dropping the condition and matching
    /// everything) — a stale link or a typo should say so.
    /// </summary>
    public static string FirstError(LagReportQueryRequest req)
    {
        foreach (var category in req.IssueCategory ?? [])
        {
            if (!Enum.TryParse<EIssueCategory>(category, out _))
            {
                return $"Unknown issueCategory '{category}'.";
            }
        }

        foreach (var tag in req.ConnectionIssueTag ?? [])
        {
            if (!Enum.TryParse<ELagReportTag>(tag, ignoreCase: true, out _))
            {
                return $"Unknown connection_issue_tag '{tag}'.";
            }
        }

        return null;
    }
}

// ── Admin aggregate ───────────────────────────────────────────────────

public static class LagReportAggregateDimensions
{
    public const string Day = "day";
    public const string NodeDay = "node-day";
    public const string Category = "category";
    public const string Server = "server";
    public const string Proxy = "proxy";
    public static readonly string[] All = [Day, NodeDay, Category, Server, Proxy];
}

/// <summary>
/// Counts over the report corpus grouped by one dimension. Inherits the list
/// endpoint's filter surface, so every filter narrows the aggregation exactly
/// as it narrows the list; Page/PageSize are ignored.
/// </summary>
public class LagReportAggregateRequest : LagReportQueryRequest
{
    public string GroupBy { get; set; }
}

/// <summary>
/// One aggregation bucket. Which key/extra fields are set depends on the
/// dimension; unset ones are omitted from the JSON.
/// </summary>
public class LagReportAggregateBucket
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Day { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ServerNodeId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string ServerNodeName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Category { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string ProxyName { get; set; }

    public long Count { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ExplicitCount { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DistinctPlayers { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<LagReportCategoryCount> TopCategories { get; set; }
}

public class LagReportCategoryCount
{
    public string Category { get; set; }
    public long Count { get; set; }
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
    [JsonPropertyName("connection_issue_tags")]
    [JsonConverter(typeof(JsonStringEnumListConverter<ELagReportTag>))]
    public List<ELagReportTag> ConnectionIssueTags { get; set; } = [];
    public int LagEventCount { get; set; }
    public int ConnectionEventCount { get; set; }
}
