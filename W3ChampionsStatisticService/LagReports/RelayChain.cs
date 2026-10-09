using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;

namespace W3ChampionsStatisticService.LagReports;

/// <summary>
/// One player's per-hop relay telemetry, fetched from the flo controller's
/// GetGameRelayTelemetry RPC: one entry per recent game connection of the player.
/// </summary>
public class PlayerRelayChain
{
    public DateTime FetchedAt { get; set; }

    public List<RelayConnectionData> Connections { get; set; } = [];
}

public class RelayConnectionData
{
    public long ConnectedUnixMs { get; set; }

    /// <summary>Legs ordered client first, game node last.</summary>
    public List<RelayLegData> Legs { get; set; } = [];
}

public class RelayLegData
{
    public string FromLabel { get; set; }
    public string ToLabel { get; set; }

    /// <summary>
    /// Kept as a free-form string (see <see cref="RelayLegStatus"/>) so a status added by a
    /// newer controller is stored verbatim instead of failing the whole fetch.
    /// </summary>
    public string Status { get; set; }

    /// <summary>The series measured at the leg's sending end (null for the client end).</summary>
    public RelaySeriesData Near { get; set; }

    /// <summary>The series measured at the leg's receiving end.</summary>
    public RelaySeriesData Far { get; set; }

    public RelayCloseLineData Close { get; set; }
}

public class RelaySeriesData
{
    /// <summary>node_player, node_quic, haproxy_fe or haproxy_be.</summary>
    public string Role { get; set; }

    /// <summary>tcp or quic.</summary>
    public string Kind { get; set; }

    public long FirstSeenUnixMs { get; set; }

    /// <summary>0 while the connection is still open.</summary>
    public long ClosedUnixMs { get; set; }

    /// <summary>Start of bucket 0; bucket i covers [start + i*BucketSecs, start + (i+1)*BucketSecs).</summary>
    public long BucketsStartUnixMs { get; set; }

    /// <summary>
    /// Width of one bucket. The controller sends 5 s buckets; wider ones mean the series was
    /// downsampled to fit the per-player size budget (see <see cref="RelayChainMerge"/>).
    /// </summary>
    public int BucketSecs { get; set; } = RelayBuckets.BucketSecs;

    /// <summary>Packed buckets, <see cref="RelayBuckets.BucketLen"/> bytes each, stored as BinData.</summary>
    [JsonIgnore]
    [BsonElement("Buckets")]
    public byte[] PackedBuckets { get; set; } = [];

    [BsonIgnore]
    public int BucketCount => (PackedBuckets?.Length ?? 0) / RelayBuckets.BucketLen;

    /// <summary>Decoded columns for the admin API; never stored.</summary>
    [BsonIgnore]
    [JsonPropertyName("buckets")]
    public RelayBucketColumns Buckets => RelayBuckets.Decode(PackedBuckets);
}

/// <summary>HAProxy's close-line summary for a relayed connection.</summary>
public class RelayCloseLineData
{
    public uint FcRttMs { get; set; }
    public uint FcRttvarMs { get; set; }
    public uint FcRetrans { get; set; }
    public uint FcLost { get; set; }
    public uint BcRttMs { get; set; }
    public string Term { get; set; }
    public ulong BytesIn { get; set; }
    public ulong BytesOut { get; set; }
    public uint DurationMs { get; set; }
}

/// <summary>
/// Parallel per-bucket arrays. A null entry means the transport does not expose that field,
/// or (all fields null) that the sampler did not run during that bucket.
/// </summary>
public class RelayBucketColumns
{
    public ushort?[] SrttMaxMs { get; set; } = [];
    public ushort?[] RttvarMaxMs { get; set; } = [];
    public ushort?[] RetransDelta { get; set; } = [];
    public ushort?[] LostMax { get; set; } = [];
    public ushort?[] UnackedMax { get; set; } = [];
    public uint?[] RxBytesDelta { get; set; } = [];
    public uint?[] TxBytesDelta { get; set; } = [];
    public byte?[] StallSecs { get; set; } = [];
}

/// <summary>Leg statuses the flo controller emits.</summary>
public static class RelayLegStatus
{
    public const string Measured = "measured";
    public const string OneSided = "one_sided";
    public const string PendingClose = "pending_close";
    public const string UnmeasuredNoFloNode = "unmeasured_no_flo_node";
    public const string UnmeasuredPortRewritten = "unmeasured_port_rewritten";
    public const string NodeTooOld = "node_too_old";
    public const string Expired = "expired";
    public const string NodeUnavailable = "node_unavailable";
    public const string HopLimit = "hop_limit";
}
