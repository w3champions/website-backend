using System.Collections.Generic;
using NUnit.Framework;
using W3ChampionsStatisticService.NetworkDiagnostics;

namespace WC3ChampionsStatisticService.Tests.NetworkDiagnostics;

[TestFixture]
public class SeriesStatsTests
{
    private static ProbeSample Ok(uint offset, double ms) => new(offset, (uint)(ms * 1000));
    private static ProbeSample Lost(uint offset) => new(offset, null);

    [Test]
    public void Compute_FourOkOneLost()
    {
        var stats = SeriesStats.Compute(new List<ProbeSample> { Ok(0, 10), Ok(1000, 20), Lost(2000), Ok(3000, 30), Ok(4000, 40) });

        Assert.AreEqual(5, stats.Sent);
        Assert.AreEqual(4, stats.Received);
        Assert.AreEqual(20.0, stats.LossPct);
        Assert.AreEqual(40.0, stats.LastMs);
        Assert.AreEqual(10.0, stats.MinMs);
        Assert.AreEqual(25.0, stats.AvgMs);
        Assert.AreEqual(40.0, stats.MaxMs);
        Assert.AreEqual(11.18, stats.StddevMs!.Value, 0.001);   // population stddev
        Assert.AreEqual(20.0, stats.P50Ms);                    // nearest rank: ceil(0.5*4)=2
        Assert.AreEqual(40.0, stats.P90Ms);                    // ceil(0.9*4)=4
        Assert.AreEqual(40.0, stats.P95Ms);
        Assert.AreEqual(40.0, stats.P99Ms);
        Assert.AreEqual(10.0, stats.JitterMs);                 // |20-10|,|30-20|,|40-30| skipping the lost one
    }

    [Test]
    public void Compute_SortsByOffset_BeforeLastAndJitter()
    {
        var stats = SeriesStats.Compute(new List<ProbeSample> { Ok(2000, 30), Ok(0, 10), Ok(1000, 50) });

        Assert.AreEqual(30.0, stats.LastMs);
        Assert.AreEqual(30.0, stats.JitterMs); // (40 + 20) / 2
    }

    [Test]
    public void Compute_AllLost_MsFieldsNull_LossHundred()
    {
        var stats = SeriesStats.Compute(new List<ProbeSample> { Lost(0), Lost(1000) });

        Assert.AreEqual(2, stats.Sent);
        Assert.AreEqual(0, stats.Received);
        Assert.AreEqual(100.0, stats.LossPct);
        Assert.IsNull(stats.MinMs);
        Assert.IsNull(stats.P50Ms);
        Assert.IsNull(stats.JitterMs);
        Assert.IsNull(stats.LastMs);
    }

    [Test]
    public void Compute_Empty_LossNull()
    {
        var stats = SeriesStats.Compute(new List<ProbeSample>());
        Assert.AreEqual(0, stats.Sent);
        Assert.IsNull(stats.LossPct);
    }

    [Test]
    public void Compute_SingleSample_JitterNull()
    {
        var stats = SeriesStats.Compute(new List<ProbeSample> { new(0, 12345) }); // µs, exact
        Assert.AreEqual(12.345, stats.P99Ms);
        Assert.AreEqual(0.0, stats.StddevMs);
        Assert.IsNull(stats.JitterMs);
    }

    [TestCase(50, 5.0)]
    [TestCase(90, 9.0)]
    [TestCase(95, 10.0)]
    [TestCase(99, 10.0)]
    public void NearestRank_TenValues(int p, double expected)
    {
        var sorted = new List<double> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        Assert.AreEqual(expected, SeriesStats.NearestRank(sorted, p));
    }

    [Test]
    public void NearestRank_Empty_IsNull() => Assert.IsNull(SeriesStats.NearestRank(new List<double>(), 50));
}
