using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using W3C.Domain.Repositories;

namespace W3ChampionsStatisticService.LagReports;

/// <summary>
/// Per-player leave reasons for one flo game, read from the flo-stats GraphQL
/// snapshot.
///
/// Deliberately a separate collection from <see cref="LagReport"/>: a LagReport
/// document only exists when a player actually submitted a report, which is
/// exactly the population that cancelled and abandoned games do NOT belong to.
/// Extending LagReport would therefore capture nothing on the games this is for.
/// </summary>
[BsonIgnoreExtraElements]
public class FloGameLeaveReport : IIdentifiable
{
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Flo game id, from flo-controller. Unique key for this collection.</summary>
    public int FloGameId { get; set; }

    /// <summary>Which handler captured this. Diagnostic - tells you which paths actually yield data.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EFloLeaveCaptureTrigger Trigger { get; set; }

    /// <summary>
    /// False when flo-stats had no snapshot for the game - evicted from its
    /// in-memory LRU, or the fetch failed.
    ///
    /// This is the field that keeps the data honest. Without it, "flo saw the game
    /// and recorded no leave for this player" is indistinguishable from "we never
    /// saw the game at all", and those support opposite conclusions.
    /// </summary>
    public bool SnapshotAvailable { get; set; }

    public List<FloPlayerLeave> Players { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>One player's leave record within a <see cref="FloGameLeaveReport"/>.</summary>
public class FloPlayerLeave
{
    /// <summary>Flo player id. Matches <see cref="ServerSidePingData.PlayerId"/>.</summary>
    public int PlayerId { get; set; }

    /// <summary>Battletag, or "Player N" when flo masked names for the game.</summary>
    public string PlayerName { get; set; }

    /// <summary>
    /// Null both when flo recorded no leave for this slot AND when flo sent a
    /// variant this backend does not know about. Use <see cref="LeaveReasonRaw"/> to
    /// tell those apart: a null raw means no record, a non-null raw means an
    /// unmapped new variant.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EFloLeaveReason? LeaveReason { get; set; }

    /// <summary>Exact wire string from flo, e.g. "LEAVE_DISCONNECT". Null when absent.</summary>
    public string LeaveReasonRaw { get; set; }

    /// <summary>
    /// Milliseconds since game start, on flo's action clock - the same clock as
    /// <see cref="LagEvent.GameTimeOffsetMs"/>, so the two are directly comparable.
    /// Null when there is no leave record.
    /// </summary>
    public long? LeftAtMs { get; set; }
}
