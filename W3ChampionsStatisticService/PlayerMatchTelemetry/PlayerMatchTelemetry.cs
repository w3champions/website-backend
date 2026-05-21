using System;
using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace W3ChampionsStatisticService.PlayerMatchTelemetry;

// Spec: docs/superpowers/specs/2026-05-21-flo-action-latency-design.md §4.8.2.
// _id == GameId. Players merged by BattleTag via upsert.
public class PlayerMatchTelemetry
{
    [BsonId]
    public long GameId { get; set; }

    public DateTime MatchWallStart { get; set; }

    public int BucketMs { get; set; }

    public List<PlayerMatchTelemetryEntry> Players { get; set; } = new();

    public DateTime CreatedAt { get; set; }

    // TTL anchor — index expires this document `expireAfterSeconds: 0` after this value.
    public DateTime ExpiresAt { get; set; }
}

public class PlayerMatchTelemetryEntry
{
    public string BattleTag { get; set; } = string.Empty;

    // Resolved server-side from the matchmaking-service game record. Nullable until joined.
    public int? FloPlayerId { get; set; }

    public string ConnectionType { get; set; } = string.Empty;   // "TCP" | "QUIC"

    // Resolved server-side from flo-controller. Nullable until joined.
    public int? ServerNodeId { get; set; }
    public string? ServerNodeName { get; set; }

    public uint GameLengthMs { get; set; }

    public bool Crashed { get; set; }

    public DisconnectStats Disconnects { get; set; } = new();

    public ActionLatencyAggregate ActionLatencyAggregate { get; set; } = new();

    public int BucketCount { get; set; }

    // BinData subtype 0; uint32 little-endian
    public BsonBinaryData GameTimeOffsetsMs { get; set; } = new(Array.Empty<byte>());

    // BinData subtype 0; uint16 little-endian
    public BsonBinaryData MeansMs { get; set; } = new(Array.Empty<byte>());

    // BinData subtype 0; uint8
    public BsonBinaryData SampleCounts { get; set; } = new(Array.Empty<byte>());

    public uint DroppedUnmatchedCount { get; set; }

    public DateTime SubmittedAt { get; set; }
}

public class DisconnectStats
{
    public uint Count { get; set; }
    public uint TotalDurationMs { get; set; }
    public uint MeanDurationMs { get; set; }
}

public class ActionLatencyAggregate
{
    public uint SampleCount { get; set; }
    public ushort P10Ms { get; set; }
    public ushort P50Ms { get; set; }
    public ushort P99Ms { get; set; }
    public ushort P999Ms { get; set; }
    public ushort MeanMs { get; set; }
    public ushort StddevMs { get; set; }
}
