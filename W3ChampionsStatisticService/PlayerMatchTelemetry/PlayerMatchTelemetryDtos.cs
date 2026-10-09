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

    /// <summary>8 h of 5 s transport buckets, the same span as <see cref="MaxTimeseriesBuckets"/>.</summary>
    public const int MaxTransportBuckets = 5_760;
    private const int MaxShortStringLength = 200;

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

    /// <summary>The client's own game-socket stats (flo client 0.18.5+); null from older clients.</summary>
    public TransportStatsDto TransportStats { get; init; }

    [StringLength(MaxShortStringLength)]
    public string ClientVersion { get; init; }

    [StringLength(MaxShortStringLength)]
    public string LauncherVersion { get; init; }

    /// <summary>How the launcher routed the game at game start; null from older launchers.</summary>
    public MatchTelemetryRoutingDto Routing { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var error in ValidateTransportStats().Concat(ValidateRouting()))
        {
            yield return error;
        }

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

    private IEnumerable<ValidationResult> ValidateTransportStats()
    {
        var ts = TransportStats;
        if (ts is null) yield break;

        var required = new (string Name, object Value)[]
        {
            (nameof(ts.GameTimeOffsetsMs), ts.GameTimeOffsetsMs),
            (nameof(ts.SampleCounts), ts.SampleCounts),
            (nameof(ts.SrttMaxMs), ts.SrttMaxMs),
            (nameof(ts.RetransDelta), ts.RetransDelta),
            (nameof(ts.RxBytesDelta), ts.RxBytesDelta),
            (nameof(ts.TxBytesDelta), ts.TxBytesDelta),
            (nameof(ts.StallSecs), ts.StallSecs),
        };
        var missing = required.Where(r => r.Value is null).Select(r => r.Name).ToList();
        if (missing.Count > 0)
        {
            yield return new ValidationResult(
                $"transportStats arrays must not be null: {string.Join(", ", missing)}",
                new[] { nameof(TransportStats) });
            yield break;
        }

        var n = ts.GameTimeOffsetsMs.Length;
        var lengths = new (string Name, int? Length)[]
        {
            (nameof(ts.SampleCounts), ts.SampleCounts.Length),
            (nameof(ts.SrttMaxMs), ts.SrttMaxMs.Length),
            (nameof(ts.RttvarMaxMs), ts.RttvarMaxMs?.Length),
            (nameof(ts.RetransDelta), ts.RetransDelta.Length),
            (nameof(ts.LostMax), ts.LostMax?.Length),
            (nameof(ts.UnackedMax), ts.UnackedMax?.Length),
            (nameof(ts.RxBytesDelta), ts.RxBytesDelta.Length),
            (nameof(ts.TxBytesDelta), ts.TxBytesDelta.Length),
            (nameof(ts.StallSecs), ts.StallSecs.Length),
        };
        var mismatched = lengths.Where(l => l.Length.HasValue && l.Length != n).ToList();
        if (mismatched.Count > 0)
        {
            yield return new ValidationResult(
                $"transportStats parallel arrays must match gameTimeOffsetsMs ({n}): " +
                string.Join(", ", mismatched.Select(l => $"{l.Name}={l.Length}")),
                new[] { nameof(TransportStats) });
        }
        if (n > MaxTransportBuckets)
        {
            yield return new ValidationResult(
                $"transportStats length {n} exceeds max {MaxTransportBuckets} buckets.",
                new[] { nameof(TransportStats) });
        }
    }

    private IEnumerable<ValidationResult> ValidateRouting()
    {
        if (Routing is null) yield break;
        if (Routing.ProxyName?.Length > MaxShortStringLength ||
            Routing.ProxyAddress?.Length > MaxShortStringLength ||
            Routing.ConnectionKind?.Length > MaxShortStringLength)
        {
            yield return new ValidationResult(
                $"routing strings must be at most {MaxShortStringLength} characters.",
                new[] { nameof(Routing) });
        }
    }
}

public record DisconnectEventDto(DateTime StartedAt, uint DurationMs);

/// <summary>
/// 5 s buckets of the client's game-socket stats as parallel arrays (flo
/// <c>TransportStatsTimeseries</c>). The nullable arrays are omitted by platforms that
/// cannot fill them (Windows: rttvar, lost; macOS: unacked; QUIC: rttvar, unacked).
/// </summary>
public record TransportStatsDto
{
    [EnumDataType(typeof(Transport))]
    public Transport Kind { get; init; }

    public uint[] GameTimeOffsetsMs { get; init; } = [];

    [JsonConverter(typeof(ByteArrayAsJsonNumberArrayConverter))]
    public byte[] SampleCounts { get; init; } = [];

    public ushort[] SrttMaxMs { get; init; } = [];
    public ushort[] RttvarMaxMs { get; init; }
    public ushort[] RetransDelta { get; init; } = [];
    public ushort[] LostMax { get; init; }
    public ushort[] UnackedMax { get; init; }
    public uint[] RxBytesDelta { get; init; } = [];
    public uint[] TxBytesDelta { get; init; } = [];

    [JsonConverter(typeof(ByteArrayAsJsonNumberArrayConverter))]
    public byte[] StallSecs { get; init; } = [];
}

public record MatchTelemetryRoutingDto
{
    public string ProxyName { get; init; }
    public string ProxyAddress { get; init; }

    /// <summary>"direct" or "proxied". A string so a new kind is stored, not rejected.</summary>
    public string ConnectionKind { get; init; }
}

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
    DateTime SubmittedAt,
    TransportStatsResponseDto? TransportStats = null,
    string? ClientVersion = null,
    string? LauncherVersion = null,
    MatchTelemetryRouting? Routing = null
);

public record TransportStatsResponseDto(
    Transport Kind,
    int BucketCount,
    uint[] GameTimeOffsetsMs,
    [property: JsonConverter(typeof(ByteArrayAsJsonNumberArrayConverter))]
    byte[] SampleCounts,
    ushort[] SrttMaxMs,
    ushort[]? RttvarMaxMs,
    ushort[] RetransDelta,
    ushort[]? LostMax,
    ushort[]? UnackedMax,
    uint[] RxBytesDelta,
    uint[] TxBytesDelta,
    [property: JsonConverter(typeof(ByteArrayAsJsonNumberArrayConverter))]
    byte[] StallSecs
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
            SubmittedAt: e.SubmittedAt,
            TransportStats: ToTransportStatsResponseDto(e.TransportStats),
            ClientVersion: e.ClientVersion,
            LauncherVersion: e.LauncherVersion,
            Routing: e.Routing
        );
    }

    private static TransportStatsResponseDto? ToTransportStatsResponseDto(TransportStatsEntry? t)
    {
        if (t is null) return null;
        return new TransportStatsResponseDto(
            Kind: t.Kind,
            BucketCount: t.BucketCount,
            GameTimeOffsetsMs: DecodeU32Le(t.GameTimeOffsetsMs),
            SampleCounts: DecodeU8(t.SampleCounts),
            SrttMaxMs: DecodeU16Le(t.SrttMaxMs),
            RttvarMaxMs: t.RttvarMaxMs is null ? null : DecodeU16Le(t.RttvarMaxMs),
            RetransDelta: DecodeU16Le(t.RetransDelta),
            LostMax: t.LostMax is null ? null : DecodeU16Le(t.LostMax),
            UnackedMax: t.UnackedMax is null ? null : DecodeU16Le(t.UnackedMax),
            RxBytesDelta: DecodeU32Le(t.RxBytesDelta),
            TxBytesDelta: DecodeU32Le(t.TxBytesDelta),
            StallSecs: DecodeU8(t.StallSecs)
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
