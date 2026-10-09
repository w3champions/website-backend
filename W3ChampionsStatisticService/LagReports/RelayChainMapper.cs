using System;
using System.Linq;
using Serilog;
using W3ChampionsStatisticService.LagReports.FloControllerGrpc;

namespace W3ChampionsStatisticService.LagReports;

/// <summary>Maps the flo controller's GetGameRelayTelemetry reply onto the stored model.</summary>
public static class RelayChainMapper
{
    public static PlayerRelayChain FromReply(GetGameRelayTelemetryReply reply, DateTime fetchedAt) => new()
    {
        FetchedAt = fetchedAt,
        Connections = reply.Connections.Select(c => new RelayConnectionData
        {
            ConnectedUnixMs = c.ConnectedUnixMs,
            Legs = c.Legs.Select(l => new RelayLegData
            {
                FromLabel = l.FromLabel,
                ToLabel = l.ToLabel,
                Status = l.Status,
                Near = MapSeries(l.Near, reply),
                Far = MapSeries(l.Far, reply),
                Close = MapClose(l.Close),
            }).ToList(),
        }).ToList(),
    };

    private static RelaySeriesData MapSeries(RelaySeries s, GetGameRelayTelemetryReply reply)
    {
        if (s == null) return null;

        var bytes = s.Buckets.ToByteArray();
        var whole = bytes.Length / RelayBuckets.BucketLen * RelayBuckets.BucketLen;
        if (whole != bytes.Length)
        {
            Log.Warning(
                "RelayTelemetry: series {Role} of game {GameId} player {PlayerId} has {Length} B, not a multiple of {BucketLen}; dropping the partial bucket",
                s.Role, reply.GameId, reply.PlayerId, bytes.Length, RelayBuckets.BucketLen);
            bytes = bytes[..whole];
        }
        else if (s.BucketCount != bytes.Length / RelayBuckets.BucketLen)
        {
            Log.Warning(
                "RelayTelemetry: series {Role} of game {GameId} player {PlayerId} claims {BucketCount} buckets but carries {Actual}",
                s.Role, reply.GameId, reply.PlayerId, s.BucketCount, bytes.Length / RelayBuckets.BucketLen);
        }

        return new RelaySeriesData
        {
            Role = s.Role,
            Kind = s.Kind,
            FirstSeenUnixMs = s.FirstSeenUnixMs,
            ClosedUnixMs = s.ClosedUnixMs,
            BucketsStartUnixMs = s.BucketsStartUnixMs,
            BucketSecs = RelayBuckets.BucketSecs,
            PackedBuckets = bytes,
        };
    }

    private static RelayCloseLineData MapClose(RelayCloseLine c) => c == null ? null : new RelayCloseLineData
    {
        FcRttMs = c.FcRttMs,
        FcRttvarMs = c.FcRttvarMs,
        FcRetrans = c.FcRetrans,
        FcLost = c.FcLost,
        BcRttMs = c.BcRttMs,
        Term = c.Term,
        BytesIn = c.BytesIn,
        BytesOut = c.BytesOut,
        DurationMs = c.DurationMs,
    };
}
