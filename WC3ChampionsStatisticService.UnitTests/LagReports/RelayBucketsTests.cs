using System.Text.Json;
using NUnit.Framework;
using W3ChampionsStatisticService.LagReports;
using static WC3ChampionsStatisticService.Tests.LagReports.RelayTelemetryTestData;

namespace WC3ChampionsStatisticService.Tests.LagReports;

[TestFixture]
public class RelayBucketsTests
{
    [Test]
    public void Decode_ReadsEachFieldLittleEndianAtItsOffset()
    {
        var packed = Pack(new B(1200, 400, 7, 3, 9, 70_000, 65_536, 2), Healthy(41));

        var c = RelayBuckets.Decode(packed);

        Assert.That(c.SrttMaxMs, Is.EqualTo(new ushort?[] { 1200, 41 }));
        Assert.That(c.RttvarMaxMs, Is.EqualTo(new ushort?[] { 400, 3 }));
        Assert.That(c.RetransDelta, Is.EqualTo(new ushort?[] { 7, 0 }));
        Assert.That(c.LostMax, Is.EqualTo(new ushort?[] { 3, 0 }));
        Assert.That(c.UnackedMax, Is.EqualTo(new ushort?[] { 9, 1 }));
        Assert.That(c.RxBytesDelta, Is.EqualTo(new uint?[] { 70_000, 900 }));
        Assert.That(c.TxBytesDelta, Is.EqualTo(new uint?[] { 65_536, 800 }));
        Assert.That(c.StallSecs, Is.EqualTo(new byte?[] { 2, 0 }));
    }

    [Test]
    public void Decode_MissingFieldSentinelsBecomeNull()
    {
        // QUIC exposes neither rttvar nor unacked: flo-node writes 0xFFFF for them.
        var packed = Pack(new B(55, 0xFFFF, 1, 2, 0xFFFF, 10, 20, 0));

        var c = RelayBuckets.Decode(packed);

        Assert.That(c.SrttMaxMs[0], Is.EqualTo(55));
        Assert.That(c.RttvarMaxMs[0], Is.Null);
        Assert.That(c.UnackedMax[0], Is.Null);
        Assert.That(c.LostMax[0], Is.EqualTo(2));
    }

    [Test]
    public void Decode_GapBucketIsAllNull()
    {
        var c = RelayBuckets.Decode(Pack(Healthy(), Gap));

        Assert.That(c.SrttMaxMs[1], Is.Null);
        Assert.That(c.RttvarMaxMs[1], Is.Null);
        Assert.That(c.RetransDelta[1], Is.Null);
        Assert.That(c.LostMax[1], Is.Null);
        Assert.That(c.UnackedMax[1], Is.Null);
        Assert.That(c.RxBytesDelta[1], Is.Null);
        Assert.That(c.TxBytesDelta[1], Is.Null);
        Assert.That(c.StallSecs[1], Is.Null);
    }

    [Test]
    public void Decode_NullOrEmptyGivesEmptyColumns()
    {
        Assert.That(RelayBuckets.Decode(null).SrttMaxMs, Is.Empty);
        Assert.That(RelayBuckets.Decode([]).StallSecs, Is.Empty);
    }

    [Test]
    public void Downsample_MergesPairsMaxForGaugesSumForDeltas()
    {
        var packed = Pack(
            new B(40, 3, 1, 0, 2, 1000, 900, 0),
            new B(1500, 0xFFFF, 2, 4, 1, 3000, 100, 1),
            new B(50, 5, 0, 0, 0, 10, 10, 0));

        var merged = RelayBuckets.Downsample(packed, 2);
        var c = RelayBuckets.Decode(merged);

        Assert.That(c.SrttMaxMs, Is.EqualTo(new ushort?[] { 1500, 50 }));
        Assert.That(c.RttvarMaxMs, Is.EqualTo(new ushort?[] { 3, 5 }), "missing values are skipped, not taken as 0xFFFF");
        Assert.That(c.RetransDelta, Is.EqualTo(new ushort?[] { 3, 0 }));
        Assert.That(c.LostMax, Is.EqualTo(new ushort?[] { 4, 0 }));
        Assert.That(c.UnackedMax, Is.EqualTo(new ushort?[] { 2, 0 }));
        Assert.That(c.RxBytesDelta, Is.EqualTo(new uint?[] { 4000, 10 }));
        Assert.That(c.TxBytesDelta, Is.EqualTo(new uint?[] { 1000, 10 }));
        Assert.That(c.StallSecs, Is.EqualTo(new byte?[] { 1, 0 }));
    }

    [Test]
    public void Downsample_AllGapGroupStaysGapAndSumsSaturate()
    {
        var packed = Pack(Gap, Gap, new B(1, 1, 60_000, 1, 1, uint.MaxValue - 1, 1, 200), new B(1, 1, 60_000, 1, 1, 5, 1, 200));

        var c = RelayBuckets.Decode(RelayBuckets.Downsample(packed, 2));

        Assert.That(c.SrttMaxMs[0], Is.Null);
        Assert.That(c.StallSecs[0], Is.Null);
        Assert.That(c.RetransDelta[1], Is.EqualTo(65_534), "a sum never collides with the 0xFFFF sentinel");
        Assert.That(c.RxBytesDelta[1], Is.EqualTo(uint.MaxValue - 1));
        Assert.That(c.StallSecs[1], Is.EqualTo(254));
    }

    [Test]
    public void Series_SerializesDecodedArraysAndNotThePackedBytes()
    {
        var series = Series("node_player", 0);
        series.PackedBuckets = Pack(Healthy(42), Gap);

        var json = JsonSerializer.Serialize(series, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var root = JsonDocument.Parse(json).RootElement;

        Assert.That(root.TryGetProperty("packedBuckets", out _), Is.False);
        Assert.That(root.GetProperty("bucketCount").GetInt32(), Is.EqualTo(2));
        Assert.That(root.GetProperty("bucketSecs").GetInt32(), Is.EqualTo(5));
        Assert.That(root.GetProperty("buckets").GetProperty("srttMaxMs").GetRawText(), Is.EqualTo("[42,null]"));
        Assert.That(root.GetProperty("buckets").GetProperty("stallSecs").GetRawText(), Is.EqualTo("[0,null]"));
    }
}
