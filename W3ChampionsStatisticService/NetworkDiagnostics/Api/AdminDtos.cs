using System;
using System.Collections.Generic;
using W3ChampionsStatisticService.NetworkDiagnostics.Results;
using W3ChampionsStatisticService.NetworkDiagnostics.Runs;

namespace W3ChampionsStatisticService.NetworkDiagnostics.Api;

/// <summary>Status code + JSON body; the controller turns it into an ObjectResult.</summary>
public sealed record NetDiagResult(int StatusCode, object Body)
{
    public static NetDiagResult Ok(object body) => new(200, body);
    public static NetDiagResult Created(object body) => new(201, body);
    public static NetDiagResult Error(int status, string error) => new(status, new { error });
}

public record RunDto(
    string Id,
    DateTime CreatedAt,
    string CreatedBy,
    RunAudience Audience,
    RunTargets Targets,
    List<string> Methods,
    bool AllowDuringGame,
    int WindowSeconds,
    DateTime? WindowStart,
    DateTime? WindowEnd,
    DateTime? CancelledAt,
    RunCounters Counters,
    int ExpectedDurationSeconds)
{
    public static RunDto From(NetworkDiagnosticRun r) => new(
        r.Id, r.CreatedAt, r.CreatedBy, r.Audience, r.Targets, r.Methods, r.AllowDuringGame, r.WindowSeconds,
        r.WindowStart, r.WindowEnd, r.CancelledAt, r.Counters,
        r.WindowSeconds + NetDiagConstants.SnapshotAllowanceSeconds);
}

/// <summary>A10: deliberately without the assignment's token.</summary>
public record AssignmentDto(
    string MeasurementId,
    string BattleTag,
    string Status,
    DateTime DeliverAfter,
    DateTime DeliverBy,
    DateTime? DeliveredAt,
    DateTime? SubmittedAt,
    ClientGeo ClientGeo);

public record RunDetailDto(RunDto Run, List<AssignmentDto> Assignments);

public record PageDto<T>(List<T> Items, long Total);

public record FloNodeDto(int Id, string Name, string Location, string CountryId, bool IsDisabled);

public record TargetsDto(List<FloNodeDto> FloNodes, IReadOnlyList<CoreServerConfig> CoreServers, IReadOnlyList<BaselineConfig> Baselines);
