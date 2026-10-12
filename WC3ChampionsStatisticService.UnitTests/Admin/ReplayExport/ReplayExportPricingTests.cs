using NUnit.Framework;
using W3ChampionsStatisticService.Admin.ReplayExport;

namespace WC3ChampionsStatisticService.Tests.Admin.ReplayExport;

[TestFixture]
public class ReplayExportPricingTests
{
    [TestCase(0, 0.00)]
    [TestCase(1, 5.00)]
    [TestCase(200, 5.00)]
    [TestCase(250, 5.00)]
    [TestCase(251, 5.02)]
    [TestCase(1000, 20.00)]
    [TestCase(10_000, 200.00)]
    public void Price_IsTwoCentsPerReplayWithAFiveDollarMinimum(int replays, decimal expected)
    {
        Assert.AreEqual(expected, ReplayExportPricing.Price(replays));
    }
}
