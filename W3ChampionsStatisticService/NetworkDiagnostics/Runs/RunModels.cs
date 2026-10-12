using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;

namespace W3ChampionsStatisticService.NetworkDiagnostics.Runs;

public static class AudienceKind
{
    public const string Users = "Users";
    public const string SampleCount = "SampleCount";
    public const string SamplePercent = "SamplePercent";
    public const string All = "All";
}

/// <summary>C6 audience: exactly one of Users{battleTags} / SampleCount{count} / SamplePercent{percent} / All.</summary>
[BsonIgnoreExtraElements]
public record RunAudience
{
    public string Kind { get; init; }
    [BsonIgnoreIfNull, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string> BattleTags { get; init; }
    [BsonIgnoreIfNull, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Count { get; init; }
    [BsonIgnoreIfNull, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Percent { get; init; }
}

[BsonIgnoreExtraElements]
public record RunTargets
{
    public List<int> FloNodeIds { get; init; }
    public bool AllFloNodes { get; init; }
    public List<string> CoreServers { get; init; }
    public List<string> Baselines { get; init; }
    public List<CustomTarget> CustomTargets { get; init; }
    public bool IncludeDefaultGateway { get; init; }
}

[BsonIgnoreExtraElements]
public record CustomTarget
{
    public string Ip { get; init; }
    public int? TcpPort { get; init; }
}

/// <summary>A resolved non-flo endpoint as pushed in C4 spec.endpoints.</summary>
[BsonIgnoreExtraElements]
public record RunEndpoint
{
    public string TargetKey { get; init; }
    public string Label { get; init; }
    public string Ip { get; init; }
    public int TcpPort { get; init; }
}

[BsonIgnoreExtraElements]
public record RunCounters
{
    public int Assigned { get; init; }
    public int Delivered { get; init; }
    public int Submitted { get; init; }
    public int Expired { get; init; }
}

[BsonIgnoreExtraElements]
public record NetworkDiagnosticRun
{
    [BsonId] public string Id { get; init; }
    public DateTime CreatedAt { get; init; }
    public string CreatedBy { get; init; }
    public RunAudience Audience { get; init; }
    public RunTargets Targets { get; init; }
    /// <summary>Core/baseline/custom endpoints frozen at creation, so later config edits never change a run.</summary>
    public List<RunEndpoint> ResolvedEndpoints { get; init; } = [];
    public List<string> Methods { get; init; } = [];
    public bool AllowDuringGame { get; init; }
    public int WindowSeconds { get; init; }
    public DateTime? WindowStart { get; init; }
    public DateTime? WindowEnd { get; init; }
    public DateTime? CancelledAt { get; init; }
    public RunCounters Counters { get; init; } = new();
    public DateTime ExpiresAt { get; init; }

    [BsonIgnore, JsonIgnore]
    public bool IsFleet => Audience?.Kind is AudienceKind.SampleCount or AudienceKind.SamplePercent or AudienceKind.All;
}

/// <summary>C6 POST runs body.</summary>
public record CreateRunRequest
{
    public RunAudience Audience { get; init; }
    public RunTargets Targets { get; init; }
    public List<string> Methods { get; init; }
    public bool AllowDuringGame { get; init; }
    public int? WindowSeconds { get; init; }
}
