using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using W3ChampionsStatisticService.LagReports;

namespace WC3ChampionsStatisticService.Tests.LagReports;

/// <summary>Builders for packed relay buckets and chains, in flo-node's 19-byte LE layout.</summary>
internal static class RelayTelemetryTestData
{
    internal record struct B(
        ushort Srtt,
        ushort Rttvar,
        ushort Retrans,
        ushort Lost,
        ushort Unacked,
        uint Rx,
        uint Tx,
        byte Stall);

    internal static readonly B Gap = new(0xFFFF, 0xFFFF, 0xFFFF, 0xFFFF, 0xFFFF, uint.MaxValue, uint.MaxValue, 0xFF);

    internal static B Healthy(ushort srtt = 40) => new(srtt, 3, 0, 0, 1, 900, 800, 0);

    internal static byte[] Pack(params B[] buckets)
    {
        var bytes = new byte[buckets.Length * 19];
        for (var i = 0; i < buckets.Length; i++)
        {
            var s = bytes.AsSpan(i * 19, 19);
            var b = buckets[i];
            BinaryPrimitives.WriteUInt16LittleEndian(s[0..2], b.Srtt);
            BinaryPrimitives.WriteUInt16LittleEndian(s[2..4], b.Rttvar);
            BinaryPrimitives.WriteUInt16LittleEndian(s[4..6], b.Retrans);
            BinaryPrimitives.WriteUInt16LittleEndian(s[6..8], b.Lost);
            BinaryPrimitives.WriteUInt16LittleEndian(s[8..10], b.Unacked);
            BinaryPrimitives.WriteUInt32LittleEndian(s[10..14], b.Rx);
            BinaryPrimitives.WriteUInt32LittleEndian(s[14..18], b.Tx);
            s[18] = b.Stall;
        }
        return bytes;
    }

    internal static byte[] PackHealthy(int count) => Pack(Enumerable.Range(0, count).Select(_ => Healthy()).ToArray());

    internal static RelaySeriesData Series(string role, int buckets, long closedUnixMs = 2_000) => new()
    {
        Role = role,
        Kind = "tcp",
        FirstSeenUnixMs = 1_000,
        ClosedUnixMs = closedUnixMs,
        BucketsStartUnixMs = 1_000,
        PackedBuckets = PackHealthy(buckets),
    };

    internal static RelayLegData Leg(string from, string to, string status, RelaySeriesData near, RelaySeriesData far) => new()
    {
        FromLabel = from,
        ToLabel = to,
        Status = status,
        Near = near,
        Far = far,
    };

    /// <summary>client → relay → node, both legs measured and closed.</summary>
    internal static RelayConnectionData RelayedConnection(long connectedUnixMs, int buckets, string status = RelayLegStatus.Measured, long closedUnixMs = 2_000) => new()
    {
        ConnectedUnixMs = connectedUnixMs,
        Legs =
        [
            Leg("client", "relay-a", status, null, Series("haproxy_fe", buckets, closedUnixMs)),
            Leg("relay-a", "node-1", status, Series("haproxy_be", buckets, closedUnixMs), Series("node_player", buckets, closedUnixMs)),
        ],
    };

    internal static PlayerRelayChain Chain(params RelayConnectionData[] connections) => new()
    {
        Connections = new List<RelayConnectionData>(connections),
    };
}
