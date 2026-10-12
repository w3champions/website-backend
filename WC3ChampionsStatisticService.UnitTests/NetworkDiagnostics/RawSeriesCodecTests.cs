using System.Collections.Generic;
using NUnit.Framework;
using W3ChampionsStatisticService.NetworkDiagnostics;

namespace WC3ChampionsStatisticService.Tests.NetworkDiagnostics;

[TestFixture]
public class RawSeriesCodecTests
{
    [Test]
    public void Encode_IsLittleEndianPairs_WithLostMarker()
    {
        var bytes = RawSeriesCodec.Encode(new List<ProbeSample> { new(1, 2), new(3, null) });

        Assert.AreEqual(new byte[] { 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF }, bytes);
    }

    [Test]
    public void RoundTrip()
    {
        var input = new List<ProbeSample> { new(0, 15234), new(1000, null), new(uint.MaxValue, uint.MaxValue - 1) };
        Assert.AreEqual(input, RawSeriesCodec.Decode(RawSeriesCodec.Encode(input)));
    }

    [Test]
    public void Decode_IgnoresTrailingPartialPair()
    {
        Assert.AreEqual(1, RawSeriesCodec.Decode(new byte[] { 1, 0, 0, 0, 2, 0, 0, 0, 9 }).Count);
    }
}
