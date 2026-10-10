using System.Collections.Generic;
using System.Linq;
using Serilog;

namespace W3ChampionsStatisticService.LagReports;

/// <summary>
/// Decides when a stored relay chain is worth fetching again, and folds a fresh fetch into
/// the stored one without losing data: the node rings expire after a while, so a later
/// fetch can return less than an earlier one did.
/// </summary>
public static class RelayChainMerge
{
    /// <summary>The controller returns up to 8; reports are about the latest ones.</summary>
    public const int MaxConnectionsPerPlayer = 4;

    /// <summary>
    /// Packed bucket bytes kept per player: a 20 min game over one relay (3 series × 240
    /// buckets) fits at full resolution, and a 4v4 where everyone reports stays near 100 KB.
    /// </summary>
    public const int MaxBucketBytesPerPlayer = 14 * 1024;

    private static readonly HashSet<string> RetryStatuses = [RelayLegStatus.PendingClose, RelayLegStatus.NodeUnavailable];

    // Statuses that report lost access to data, not a newer verdict about it.
    private static readonly HashSet<string> DataLossStatuses = [RelayLegStatus.Expired, RelayLegStatus.NodeUnavailable, RelayLegStatus.NodeTooOld];

    /// <summary>
    /// Whether a trigger (submit, match end, match cancel) should fetch the chain again: unless
    /// every leg is terminal, a controller or host update since the last fetch may have improved
    /// it. Any status not known to be terminal counts as open, including ones from a newer controller.
    /// </summary>
    public static bool NeedsRefresh(PlayerRelayChain chain)
    {
        if (chain?.Connections == null || chain.Connections.Count == 0) return true;

        return chain.Connections.Any(c => !IsFinal(c));
    }

    /// <summary>
    /// Whether a fetch just made is worth repeating within the same trigger: only a connection
    /// that is still closing or a node that did not answer resolves by itself, a stale status
    /// waits for the next trigger.
    /// </summary>
    public static bool IsStillClosing(PlayerRelayChain chain) => chain?.Connections != null &&
        chain.Connections.Where(c => !IsFinal(c)).SelectMany(c => c.Legs ?? []).Any(l =>
            RetryStatuses.Contains(l.Status) || IsOpenNodeSeries(l.Near) || IsOpenNodeSeries(l.Far));

    private static bool IsFinal(RelayConnectionData connection)
    {
        var legs = connection.Legs ?? [];
        return legs.Count > 0 && legs.Select((leg, i) => IsFinal(leg, isLast: i == legs.Count - 1)).All(final => final);
    }

    // Close is the far relay's close line, so the leg into the game node never has one.
    private static bool IsFinal(RelayLegData leg, bool isLast) => leg.Status switch
    {
        RelayLegStatus.Expired => true,
        RelayLegStatus.Measured => (isLast || leg.Close != null) && !IsOpenNodeSeries(leg.Near) && !IsOpenNodeSeries(leg.Far),
        _ => false,
    };

    // Only the game node's own series says the connection is still open: a relay may keep its
    // socket entry a little longer and must not cause a refresh of a finished connection.
    private static bool IsOpenNodeSeries(RelaySeriesData s) =>
        s != null && s.ClosedUnixMs == 0 && s.Role is "node_player" or "node_quic";

    public static PlayerRelayChain Merge(PlayerRelayChain existing, PlayerRelayChain fresh)
    {
        var byStart = new Dictionary<long, RelayConnectionData>();
        foreach (var old in existing?.Connections ?? [])
        {
            byStart.TryAdd(old.ConnectedUnixMs, old);
        }
        foreach (var next in fresh.Connections)
        {
            byStart[next.ConnectedUnixMs] = byStart.TryGetValue(next.ConnectedUnixMs, out var old)
                ? MergeConnection(old, next)
                : next;
        }

        var kept = byStart.Values
            .OrderBy(c => c.ConnectedUnixMs)
            .TakeLast(MaxConnectionsPerPlayer)
            .ToList();
        if (kept.Count < byStart.Count)
        {
            Log.Information("RelayTelemetry: kept the latest {Kept} of {Total} connections", kept.Count, byStart.Count);
        }

        return new PlayerRelayChain
        {
            FetchedAt = fresh.FetchedAt,
            Connections = FitBudget(kept),
        };
    }

    private static RelayConnectionData MergeConnection(RelayConnectionData old, RelayConnectionData next)
    {
        var samePath = old.Legs.Count == next.Legs.Count &&
            old.Legs.Zip(next.Legs).All(p => p.First.FromLabel == p.Second.FromLabel && p.First.ToLabel == p.Second.ToLabel);
        if (!samePath)
        {
            return CoveredSecs(old.Legs) > CoveredSecs(next.Legs) ? old : next;
        }

        return new RelayConnectionData
        {
            ConnectedUnixMs = next.ConnectedUnixMs,
            Legs = old.Legs.Zip(next.Legs).Select((p, i) => MergeLeg(p.First, p.Second, isLast: i == old.Legs.Count - 1)).ToList(),
        };
    }

    // The two ends come from different nodes, so one can expire while the other still answers.
    private static RelayLegData MergeLeg(RelayLegData old, RelayLegData next, bool isLast) => new()
    {
        FromLabel = next.FromLabel,
        ToLabel = next.ToLabel,
        Status = KeepsOldStatus(old, next, isLast) ? old.Status : next.Status,
        Near = PickSeries(old.Near, next.Near),
        Far = PickSeries(old.Far, next.Far),
        Close = next.Close ?? old.Close,
    };

    // A terminal verdict survives a later loss of access; a non-terminal one must not hide
    // the fresh status, or a transient failure would read as unchanged.
    private static bool KeepsOldStatus(RelayLegData old, RelayLegData next, bool isLast) =>
        DataLossStatuses.Contains(next.Status) && IsFinal(old, isLast) &&
        (old.Status == RelayLegStatus.Expired || CoveredSecs([old]) > 0);

    private static RelaySeriesData PickSeries(RelaySeriesData old, RelaySeriesData next) =>
        Covered(old) > Covered(next) ? old : next ?? old;

    private static long Covered(RelaySeriesData s) => s == null ? 0 : (long)s.BucketCount * s.BucketSecs;

    private static long CoveredSecs(IEnumerable<RelayLegData> legs) => legs
        .SelectMany(l => new[] { l.Near, l.Far })
        .Sum(Covered);

    private static List<RelayConnectionData> FitBudget(List<RelayConnectionData> connections)
    {
        var counts = connections
            .SelectMany(c => c.Legs)
            .SelectMany(l => new[] { l.Near, l.Far })
            .Where(s => s != null)
            .Select(s => s.BucketCount)
            .ToList();
        if (counts.Count == 0) return connections;

        var longest = counts.Max();
        var factor = 1;
        while (factor < longest && counts.Sum(n => (long)(n + factor - 1) / factor) * RelayBuckets.BucketLen > MaxBucketBytesPerPlayer)
        {
            factor *= 2;
        }
        if (factor == 1) return connections;
        Log.Information("RelayTelemetry: downsampled {SeriesCount} series by {Factor} to fit {Budget} B", counts.Count, factor, MaxBucketBytesPerPlayer);

        return connections.Select(c => new RelayConnectionData
        {
            ConnectedUnixMs = c.ConnectedUnixMs,
            Legs = c.Legs.Select(l => new RelayLegData
            {
                FromLabel = l.FromLabel,
                ToLabel = l.ToLabel,
                Status = l.Status,
                Near = Downsample(l.Near, factor),
                Far = Downsample(l.Far, factor),
                Close = l.Close,
            }).ToList(),
        }).ToList();
    }

    private static RelaySeriesData Downsample(RelaySeriesData s, int factor) => s == null ? null : new RelaySeriesData
    {
        Role = s.Role,
        Kind = s.Kind,
        FirstSeenUnixMs = s.FirstSeenUnixMs,
        ClosedUnixMs = s.ClosedUnixMs,
        BucketsStartUnixMs = s.BucketsStartUnixMs,
        BucketSecs = s.BucketSecs * factor,
        PackedBuckets = RelayBuckets.Downsample(s.PackedBuckets, factor),
    };
}
