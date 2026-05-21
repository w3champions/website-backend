#nullable enable

using System;
using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using W3ChampionsStatisticService.LagReports;

namespace W3ChampionsStatisticService.PlayerMatchTelemetry;

// Per-game telemetry document. _id == GameId. Each player's submission is
// merged into Players[] via an idempotent upsert (one entry per BattleTag).
[BsonIgnoreExtraElements]
public class PlayerMatchTelemetry
{
    [BsonId]
    public long GameId { get; set; }

    public DateTime MatchWallStart { get; set; }

    public List<PlayerMatchTelemetryEntry> Players { get; set; } = new();

    public DateTime CreatedAt { get; set; }

    // TTL anchor — index expires this document `expireAfterSeconds: 0` after this value.
    public DateTime ExpiresAt { get; set; }
}

public class PlayerMatchTelemetryEntry
{
    public string BattleTag { get; set; } = string.Empty;

    public Transport ConnectionType { get; set; } = Transport.TCP;

    public uint GameLengthMs { get; set; }

    /// <summary>UTC timestamp when the launcher detected a crash; null when the game ended cleanly.</summary>
    public DateTime? CrashedAt { get; set; }

    public List<DisconnectEvent> DisconnectEvents { get; set; } = new();

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

/// <summary>
/// A single disconnect that occurred during the match. The launcher emits one
/// entry per disconnection with the wall-clock start time and total duration.
/// </summary>
public class DisconnectEvent
{
    public DateTime StartedAt { get; set; }
    public uint DurationMs { get; set; }
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
