using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace W3ChampionsStatisticService.PlayerMatchTelemetry;

// Spec: docs/superpowers/specs/2026-05-21-flo-action-latency-design.md §4.8.1.
//
// Launcher-e serializes via Rust serde with snake_case field names, so every
// property needs an explicit [JsonPropertyName] to bind correctly under
// ASP.NET Core's System.Text.Json defaults (which only case-fold PascalCase
// ↔ camelCase, never snake_case). Mirrors the convention used by
// LagReports/LagReportDtos.cs.
public record PlayerMatchTelemetrySubmissionDto(
    [property: JsonPropertyName("game_id")]
    [property: Range(1L, long.MaxValue)] long GameId,
    [property: JsonPropertyName("bucket_ms")]
    [property: Range(100, 10_000)] int BucketMs,
    [property: JsonPropertyName("match_wall_start")]
    DateTime MatchWallStart,
    [property: JsonPropertyName("game_length_ms")]
    uint GameLengthMs,
    [property: JsonPropertyName("crashed")]
    bool Crashed,
    [property: JsonPropertyName("connection_type")]
    [property: RegularExpression("^(TCP|QUIC)$",
        ErrorMessage = "ConnectionType must be 'TCP' or 'QUIC'.")]
    string ConnectionType,
    [property: JsonPropertyName("disconnects")]
    [property: Required] DisconnectsDto Disconnects,
    [property: JsonPropertyName("action_latency_aggregate")]
    [property: Required] ActionLatencyAggregateDto ActionLatencyAggregate,
    [property: JsonPropertyName("action_latency_timeseries")]
    [property: Required] ActionLatencyTimeseriesDto ActionLatencyTimeseries,
    [property: JsonPropertyName("dropped_unmatched_count")]
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

public record DisconnectsDto(
    [property: JsonPropertyName("count")] uint Count,
    [property: JsonPropertyName("total_duration_ms")] uint TotalDurationMs,
    [property: JsonPropertyName("mean_duration_ms")] uint MeanDurationMs);

public record ActionLatencyAggregateDto(
    [property: JsonPropertyName("sample_count")] uint SampleCount,
    [property: JsonPropertyName("p10_ms")] ushort P10Ms,
    [property: JsonPropertyName("p50_ms")] ushort P50Ms,
    [property: JsonPropertyName("p99_ms")] ushort P99Ms,
    [property: JsonPropertyName("p999_ms")] ushort P999Ms,
    [property: JsonPropertyName("mean_ms")] ushort MeanMs,
    [property: JsonPropertyName("stddev_ms")] ushort StddevMs
);

public record ActionLatencyTimeseriesDto(
    [property: JsonPropertyName("game_time_offsets_ms")] uint[] GameTimeOffsetsMs,
    [property: JsonPropertyName("means_ms")] ushort[] MeansMs,
    [property: JsonPropertyName("sample_counts")]
    [property: JsonConverter(typeof(ByteArrayAsJsonNumberArrayConverter))]
    byte[] SampleCounts
);

/// <summary>
/// Reads/writes a <c>byte[]</c> as a JSON array of unsigned integers (e.g.
/// <c>[5, 7, 6]</c>) instead of System.Text.Json's default base64 string
/// encoding. Launcher-e's Rust serde serializes <c>Vec&lt;u8&gt;</c> as a
/// number array on the wire, so this converter is required for
/// <see cref="ActionLatencyTimeseriesDto.SampleCounts"/> binding.
/// </summary>
public sealed class ByteArrayAsJsonNumberArrayConverter : JsonConverter<byte[]>
{
    public override byte[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException(
                $"Expected start of array for byte[] (sample_counts), got {reader.TokenType}.");
        }
        var buffer = new List<byte>(capacity: 256);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.Number)
            {
                throw new JsonException(
                    $"Expected number element in byte[] (sample_counts), got {reader.TokenType}.");
            }
            buffer.Add(reader.GetByte()); // Throws OverflowException-derived JsonException if out of 0..255.
        }
        return buffer.ToArray();
    }

    public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var b in value)
        {
            writer.WriteNumberValue(b);
        }
        writer.WriteEndArray();
    }
}
