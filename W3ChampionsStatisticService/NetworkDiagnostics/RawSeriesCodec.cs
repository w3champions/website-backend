using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace W3ChampionsStatisticService.NetworkDiagnostics;

/// <summary>Spec §6.5: (offsetMs u32, rttMicros u32 | 0xFFFFFFFF lost) pairs, little-endian (PlayerMatchTelemetry precedent).</summary>
public static class RawSeriesCodec
{
    public const uint LostMarker = 0xFFFFFFFF;
    private const int PairBytes = 8;

    public static byte[] Encode(IReadOnlyList<ProbeSample> samples)
    {
        var bytes = new byte[samples.Count * PairBytes];
        for (var i = 0; i < samples.Count; i++)
        {
            var span = bytes.AsSpan(i * PairBytes, PairBytes);
            BinaryPrimitives.WriteUInt32LittleEndian(span, samples[i].OffsetMs);
            BinaryPrimitives.WriteUInt32LittleEndian(span[4..], samples[i].RttUs ?? LostMarker);
        }
        return bytes;
    }

    public static List<ProbeSample> Decode(byte[] bytes)
    {
        var count = (bytes?.Length ?? 0) / PairBytes;
        var samples = new List<ProbeSample>(count);
        for (var i = 0; i < count; i++)
        {
            var span = bytes.AsSpan(i * PairBytes, PairBytes);
            var rtt = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
            samples.Add(new ProbeSample(BinaryPrimitives.ReadUInt32LittleEndian(span), rtt == LostMarker ? null : rtt));
        }
        return samples;
    }
}
