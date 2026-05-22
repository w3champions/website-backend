using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using W3ChampionsStatisticService.LagReports;

namespace W3ChampionsStatisticService.PlayerMatchTelemetry;

// Submission DTO for POST /api/player-match-telemetry. Mirrors the wire
// shape emitted by launcher-e: camelCase property names, "TCP" / "QUIC"
// transport literals, ISO-8601 timestamps. ASP.NET Core's [FromBody]
// binding uses JsonSerializerDefaults.Web (PropertyNamingPolicy =
// CamelCase, PropertyNameCaseInsensitive = true), so PascalCase C#
// properties map automatically without [JsonPropertyName] attributes.
public record PlayerMatchTelemetrySubmissionDto : IValidatableObject
{
    private const int MaxTimeseriesBuckets = 28_800;

    [Range(1L, long.MaxValue)]
    public long GameId { get; init; }

    public DateTime MatchWallStart { get; init; }

    public uint GameLengthMs { get; init; }

    public DateTime? CrashedAt { get; init; }

    [EnumDataType(typeof(Transport))]
    public Transport ConnectionType { get; init; }

    [Required]
    public List<DisconnectEventDto> DisconnectEvents { get; init; } = new();

    [Required]
    public ActionLatencyAggregateDto ActionLatencyAggregate { get; init; } = new(0, 0, 0, 0, 0, 0, 0);

    [Required]
    public ActionLatencyTimeseriesDto ActionLatencyTimeseries { get; init; } =
        new(Array.Empty<uint>(), Array.Empty<ushort>(), Array.Empty<byte>());

    public uint DroppedUnmatchedCount { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var ts = ActionLatencyTimeseries;
        if (ts is null) yield break;
        if (ts.GameTimeOffsetsMs.Length != ts.MeansMs.Length ||
            ts.MeansMs.Length != ts.SampleCounts.Length)
        {
            yield return new ValidationResult(
                $"Timeseries parallel arrays must have the same length: " +
                $"gameTimeOffsetsMs={ts.GameTimeOffsetsMs.Length}, " +
                $"meansMs={ts.MeansMs.Length}, " +
                $"sampleCounts={ts.SampleCounts.Length}",
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

public record DisconnectEventDto(DateTime StartedAt, uint DurationMs);

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
                $"Expected start of array for byte[] (sampleCounts), got {reader.TokenType}.");
        }
        var buffer = new List<byte>(capacity: 256);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.Number)
            {
                throw new JsonException(
                    $"Expected number element in byte[] (sampleCounts), got {reader.TokenType}.");
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
// Projection of the domain model. BsonBinaryData fields are decoded
// server-side into plain uint[] / ushort[] / byte[] so the frontend
// receives ordinary JSON number arrays with no MongoDB Extended JSON
// envelopes to unwrap.
//
// Wire shape: camelCase property names (matches website's TypeScript
// IPlayerMatchTelemetry types). ASP.NET Core's default
// JsonSerializerOptions.PropertyNamingPolicy = CamelCase auto-converts
// PascalCase C# names — no [JsonPropertyName] attrs needed.
// ─────────────────────────────────────────────────────────

#nullable enable
public record PlayerMatchTelemetryEntryResponseDto(
    string BattleTag,
    Transport ConnectionType,
    uint GameLengthMs,
    DateTime? CrashedAt,
    List<DisconnectEvent> DisconnectEvents,
    ActionLatencyAggregate ActionLatencyAggregate,
    int BucketCount,
    uint[] GameTimeOffsetsMs,
    ushort[] MeansMs,
    [property: JsonConverter(typeof(ByteArrayAsJsonNumberArrayConverter))]
    byte[] SampleCounts,
    uint DroppedUnmatchedCount,
    DateTime SubmittedAt
);

public record PlayerMatchTelemetryResponseDto(
    long GameId,
    DateTime MatchWallStart,
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
            Players: doc.Players.Select(ToEntryResponseDto).ToList(),
            CreatedAt: doc.CreatedAt,
            ExpiresAt: doc.ExpiresAt
        );
    }

    private static PlayerMatchTelemetryEntryResponseDto ToEntryResponseDto(PlayerMatchTelemetryEntry e)
    {
        return new PlayerMatchTelemetryEntryResponseDto(
            BattleTag: e.BattleTag,
            ConnectionType: e.ConnectionType,
            GameLengthMs: e.GameLengthMs,
            CrashedAt: e.CrashedAt,
            DisconnectEvents: e.DisconnectEvents,
            ActionLatencyAggregate: e.ActionLatencyAggregate,
            BucketCount: e.BucketCount,
            GameTimeOffsetsMs: DecodeU32Le(e.GameTimeOffsetsMs),
            MeansMs: DecodeU16Le(e.MeansMs),
            SampleCounts: DecodeU8(e.SampleCounts),
            DroppedUnmatchedCount: e.DroppedUnmatchedCount,
            SubmittedAt: e.SubmittedAt
        );
    }

    /// <summary>
    /// Decodes a <see cref="BsonBinaryData"/> blob of little-endian uint32 values
    /// into a <c>uint[]</c>. Portable across host endianness — does not rely on
    /// <see cref="Buffer.BlockCopy"/>.
    /// </summary>
    private static uint[] DecodeU32Le(BsonBinaryData? bin)
    {
        var bytes = bin?.Bytes ?? Array.Empty<byte>();
        if (bytes.Length % 4 != 0)
        {
            throw new InvalidDataException(
                $"DecodeU32Le: byte length {bytes.Length} is not divisible by 4");
        }
        var span = bytes.AsSpan();
        var result = new uint[bytes.Length / 4];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(i * 4, 4));
        }
        return result;
    }

    /// <summary>
    /// Decodes a <see cref="BsonBinaryData"/> blob of little-endian uint16 values
    /// into a <c>ushort[]</c>. Portable across host endianness.
    /// </summary>
    private static ushort[] DecodeU16Le(BsonBinaryData? bin)
    {
        var bytes = bin?.Bytes ?? Array.Empty<byte>();
        if (bytes.Length % 2 != 0)
        {
            throw new InvalidDataException(
                $"DecodeU16Le: byte length {bytes.Length} is not divisible by 2");
        }
        var span = bytes.AsSpan();
        var result = new ushort[bytes.Length / 2];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(i * 2, 2));
        }
        return result;
    }

    private static byte[] DecodeU8(BsonBinaryData? bin) => bin?.Bytes ?? Array.Empty<byte>();
}
#nullable disable
