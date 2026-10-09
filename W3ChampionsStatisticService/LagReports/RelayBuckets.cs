using System;
using System.Buffers.Binary;

namespace W3ChampionsStatisticService.LagReports;

/// <summary>
/// Codec for flo-node's packed 5 s relay buckets. Layout of one bucket, little-endian:
/// srtt_max_ms u16, rttvar_max_ms u16, retrans_delta u16, lost_max u16, unacked_max u16,
/// rx_bytes_delta u32, tx_bytes_delta u32, stall_secs u8. An all-ones field is "not exposed";
/// an all-ones bucket is a sampler gap. Real values are clamped below the sentinel by the node.
/// </summary>
public static class RelayBuckets
{
    public const int BucketLen = 19;
    public const int BucketSecs = 5;

    private const ushort MissingU16 = ushort.MaxValue;
    private const uint MissingU32 = uint.MaxValue;
    private const byte MissingU8 = byte.MaxValue;

    public static RelayBucketColumns Decode(byte[] packed)
    {
        var count = (packed?.Length ?? 0) / BucketLen;
        var c = new RelayBucketColumns
        {
            SrttMaxMs = new ushort?[count],
            RttvarMaxMs = new ushort?[count],
            RetransDelta = new ushort?[count],
            LostMax = new ushort?[count],
            UnackedMax = new ushort?[count],
            RxBytesDelta = new uint?[count],
            TxBytesDelta = new uint?[count],
            StallSecs = new byte?[count],
        };
        for (var i = 0; i < count; i++)
        {
            var b = packed.AsSpan(i * BucketLen, BucketLen);
            c.SrttMaxMs[i] = U16(b, 0);
            c.RttvarMaxMs[i] = U16(b, 2);
            c.RetransDelta[i] = U16(b, 4);
            c.LostMax[i] = U16(b, 6);
            c.UnackedMax[i] = U16(b, 8);
            c.RxBytesDelta[i] = U32(b, 10);
            c.TxBytesDelta[i] = U32(b, 14);
            c.StallSecs[i] = b[18] == MissingU8 ? null : b[18];
        }
        return c;
    }

    /// <summary>
    /// Merges each run of <paramref name="factor"/> buckets into one: maxima for the gauges
    /// (srtt, rttvar, lost, unacked), saturating sums for the deltas and stall seconds.
    /// Missing values are skipped, so a merged field is missing only if all inputs were.
    /// </summary>
    public static byte[] Downsample(byte[] packed, int factor)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(factor, 1);
        var count = (packed?.Length ?? 0) / BucketLen;
        var merged = new byte[(count + factor - 1) / factor * BucketLen];
        for (var group = 0; group * factor < count; group++)
        {
            var first = group * factor;
            var groupLen = Math.Min(factor, count - first);
            var src = packed.AsSpan(first * BucketLen, groupLen * BucketLen);
            var dst = merged.AsSpan(group * BucketLen, BucketLen);
            WriteU16(dst, 0, MaxU16(src, 0));
            WriteU16(dst, 2, MaxU16(src, 2));
            WriteU16(dst, 4, SumU16(src, 4));
            WriteU16(dst, 6, MaxU16(src, 6));
            WriteU16(dst, 8, MaxU16(src, 8));
            WriteU32(dst, 10, SumU32(src, 10));
            WriteU32(dst, 14, SumU32(src, 14));
            dst[18] = SumU8(src, 18);
        }
        return merged;
    }

    private static ushort? U16(ReadOnlySpan<byte> b, int at)
    {
        var v = BinaryPrimitives.ReadUInt16LittleEndian(b[at..]);
        return v == MissingU16 ? null : v;
    }

    private static uint? U32(ReadOnlySpan<byte> b, int at)
    {
        var v = BinaryPrimitives.ReadUInt32LittleEndian(b[at..]);
        return v == MissingU32 ? null : v;
    }

    private static ushort MaxU16(ReadOnlySpan<byte> group, int at)
    {
        ushort? max = null;
        for (var i = 0; i * BucketLen < group.Length; i++)
        {
            var v = U16(group[(i * BucketLen)..], at);
            if (v.HasValue && (!max.HasValue || v > max)) max = v;
        }
        return max ?? MissingU16;
    }

    private static ushort SumU16(ReadOnlySpan<byte> group, int at)
    {
        ulong? sum = null;
        for (var i = 0; i * BucketLen < group.Length; i++)
        {
            var v = U16(group[(i * BucketLen)..], at);
            if (v.HasValue) sum = (sum ?? 0) + v.Value;
        }
        return sum.HasValue ? (ushort)Math.Min(sum.Value, MissingU16 - 1) : MissingU16;
    }

    private static uint SumU32(ReadOnlySpan<byte> group, int at)
    {
        ulong? sum = null;
        for (var i = 0; i * BucketLen < group.Length; i++)
        {
            var v = U32(group[(i * BucketLen)..], at);
            if (v.HasValue) sum = (sum ?? 0) + v.Value;
        }
        return sum.HasValue ? (uint)Math.Min(sum.Value, MissingU32 - 1) : MissingU32;
    }

    private static byte SumU8(ReadOnlySpan<byte> group, int at)
    {
        int? sum = null;
        for (var i = 0; i * BucketLen < group.Length; i++)
        {
            var v = group[i * BucketLen + at];
            if (v != MissingU8) sum = (sum ?? 0) + v;
        }
        return sum.HasValue ? (byte)Math.Min(sum.Value, MissingU8 - 1) : MissingU8;
    }

    private static void WriteU16(Span<byte> b, int at, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(b[at..], v);

    private static void WriteU32(Span<byte> b, int at, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b[at..], v);
}
