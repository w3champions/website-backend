using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace W3ChampionsStatisticService.PlayerMatchTelemetry;

// Spec: docs/superpowers/specs/2026-05-21-flo-action-latency-design.md §4.8.1.
public record PlayerMatchTelemetrySubmissionDto(
    [property: Range(1L, long.MaxValue)] long GameId,
    [property: Range(100, 10_000)] int BucketMs,
    DateTime MatchWallStart,
    uint GameLengthMs,
    bool Crashed,
    [property: RegularExpression("^(TCP|QUIC)$",
        ErrorMessage = "ConnectionType must be 'TCP' or 'QUIC'.")]
    string ConnectionType,
    [property: Required] DisconnectsDto Disconnects,
    [property: Required] ActionLatencyAggregateDto ActionLatencyAggregate,
    [property: Required] ActionLatencyTimeseriesDto ActionLatencyTimeseries,
    uint DroppedUnmatchedCount
) : IValidatableObject
{
    private const int MaxTimeseriesBuckets = 28_800;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var ts = ActionLatencyTimeseries;
        if (ts is null) yield break;
        if (ts.GameTimeOffsetsMs.Length != ts.MeansMs.Length ||
            ts.MeansMs.Length != ts.SampleCounts.Length)
        {
            yield return new ValidationResult(
                $"Timeseries parallel arrays must be the same length: " +
                $"GameTimeOffsetsMs={ts.GameTimeOffsetsMs.Length}, " +
                $"MeansMs={ts.MeansMs.Length}, " +
                $"SampleCounts={ts.SampleCounts.Length}",
                new[] { nameof(ActionLatencyTimeseries) });
        }
        if (ts.MeansMs.Length > MaxTimeseriesBuckets)
        {
            yield return new ValidationResult(
                $"Timeseries length {ts.MeansMs.Length} exceeds max {MaxTimeseriesBuckets} buckets.",
                new[] { nameof(ActionLatencyTimeseries) });
        }
    }
}

public record DisconnectsDto(uint Count, uint TotalDurationMs, uint MeanDurationMs);

public record ActionLatencyAggregateDto(
    uint SampleCount,
    ushort P10Ms,
    ushort P50Ms,
    ushort P99Ms,
    ushort P999Ms,
    ushort MeanMs,
    ushort StddevMs
);

public record ActionLatencyTimeseriesDto(
    uint[] GameTimeOffsetsMs,
    ushort[] MeansMs,
    byte[] SampleCounts
);
