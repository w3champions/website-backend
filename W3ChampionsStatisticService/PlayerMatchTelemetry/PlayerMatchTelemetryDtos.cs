using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MongoDB.Bson;

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

// ─────────────────────────────────────────────────────────
// Response DTOs for GET /api/player-match-telemetry/by-game/{gameId}
// Projection of the domain model with BsonBinaryData fields
// exposed as MongoDB Extended JSON v2 envelope shapes
// ({ "$binary": { "base64": "...", "subType": "00" } }) so the
// website's IBinData decoder can parse them. Returning the raw
// domain model crashes System.Text.Json because BsonBinaryData has
// no serializer registered — that bug is what this DTO layer fixes.
//
// Wire shape convention: response DTOs emit camelCase to match the
// website's TypeScript IPlayerMatchTelemetry types (battleTag,
// gameId, sampleCounts, etc.). ASP.NET Core's default
// JsonSerializerOptions.PropertyNamingPolicy = CamelCase auto-converts
// PascalCase C# names — no [JsonPropertyName] needed on these records.
//
// Exception: BinDataEnvelopeDto/BinDataPayloadDto keep explicit
// [JsonPropertyName] attrs because their wire keys ($binary, base64,
// subType) are literal MongoDB Extended JSON v2 strings, not
// naming-policy-derived.
// ─────────────────────────────────────────────────────────

public record BinDataEnvelopeDto(
    [property: JsonPropertyName("$binary")] BinDataPayloadDto Binary
);

public record BinDataPayloadDto(
    [property: JsonPropertyName("base64")] string Base64,
    [property: JsonPropertyName("subType")] string SubType
);

// Response-side aggregates. Distinct from the submission-side
// DisconnectsDto/ActionLatencyAggregateDto so the GET response can emit
// camelCase keys (e.g. totalDurationMs) without the snake_case
// [JsonPropertyName] attrs that the submission DTOs need for Rust serde
// compatibility on the POST side.
public record DisconnectsResponseDto(
    uint Count,
    uint TotalDurationMs,
    uint MeanDurationMs
);

public record ActionLatencyAggregateResponseDto(
    uint SampleCount,
    ushort P10Ms,
    ushort P50Ms,
    ushort P99Ms,
    ushort P999Ms,
    ushort MeanMs,
    ushort StddevMs
);

#nullable enable
public record PlayerMatchTelemetryEntryResponseDto(
    string BattleTag,
    int? FloPlayerId,
    string ConnectionType,
    int? ServerNodeId,
    string? ServerNodeName,
    uint GameLengthMs,
    bool Crashed,
    DisconnectsResponseDto Disconnects,
    ActionLatencyAggregateResponseDto ActionLatencyAggregate,
    int BucketCount,
    BinDataEnvelopeDto GameTimeOffsetsMs,
    BinDataEnvelopeDto MeansMs,
    BinDataEnvelopeDto SampleCounts,
    uint DroppedUnmatchedCount,
    DateTime SubmittedAt
);

public record PlayerMatchTelemetryResponseDto(
    long GameId,
    DateTime MatchWallStart,
    int BucketMs,
    List<PlayerMatchTelemetryEntryResponseDto> Players,
    DateTime CreatedAt,
    DateTime ExpiresAt
);

public static class PlayerMatchTelemetryMapper
{
    public static PlayerMatchTelemetryResponseDto ToResponseDto(PlayerMatchTelemetry doc)
    {
        return new PlayerMatchTelemetryResponseDto(
            GameId: doc.GameId,
            MatchWallStart: doc.MatchWallStart,
            BucketMs: doc.BucketMs,
            Players: doc.Players.Select(ToEntryResponseDto).ToList(),
            CreatedAt: doc.CreatedAt,
            ExpiresAt: doc.ExpiresAt
        );
    }

    private static PlayerMatchTelemetryEntryResponseDto ToEntryResponseDto(PlayerMatchTelemetryEntry e)
    {
        return new PlayerMatchTelemetryEntryResponseDto(
            BattleTag: e.BattleTag,
            FloPlayerId: e.FloPlayerId,
            ConnectionType: e.ConnectionType,
            ServerNodeId: e.ServerNodeId,
            ServerNodeName: e.ServerNodeName,
            GameLengthMs: e.GameLengthMs,
            Crashed: e.Crashed,
            Disconnects: new DisconnectsResponseDto(
                e.Disconnects.Count,
                e.Disconnects.TotalDurationMs,
                e.Disconnects.MeanDurationMs),
            ActionLatencyAggregate: new ActionLatencyAggregateResponseDto(
                e.ActionLatencyAggregate.SampleCount,
                e.ActionLatencyAggregate.P10Ms,
                e.ActionLatencyAggregate.P50Ms,
                e.ActionLatencyAggregate.P99Ms,
                e.ActionLatencyAggregate.P999Ms,
                e.ActionLatencyAggregate.MeanMs,
                e.ActionLatencyAggregate.StddevMs
            ),
            BucketCount: e.BucketCount,
            GameTimeOffsetsMs: ToEnvelope(e.GameTimeOffsetsMs),
            MeansMs: ToEnvelope(e.MeansMs),
            SampleCounts: ToEnvelope(e.SampleCounts),
            DroppedUnmatchedCount: e.DroppedUnmatchedCount,
            SubmittedAt: e.SubmittedAt
        );
    }

    // Encodes a BsonBinaryData as a MongoDB Extended JSON v2 envelope:
    //   { "$binary": { "base64": "...", "subType": "00" } }
    // The website's IBinData decoder reads $binary.base64 (ignores subType),
    // but we emit subType anyway to stay spec-compliant.
    private static BinDataEnvelopeDto ToEnvelope(BsonBinaryData bin)
    {
        var base64 = Convert.ToBase64String(bin?.Bytes ?? Array.Empty<byte>());
        var subType = ((byte)(bin?.SubType ?? BsonBinarySubType.Binary)).ToString("X2");
        return new BinDataEnvelopeDto(new BinDataPayloadDto(base64, subType));
    }
}
#nullable disable
