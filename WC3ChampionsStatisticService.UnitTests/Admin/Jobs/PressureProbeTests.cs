using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.Admin.Jobs;

namespace WC3ChampionsStatisticService.Tests.Admin.Jobs;

[TestFixture]
public class PressureProbeTests : IntegrationTestBase
{
    /// <summary>
    /// The probe reads WiredTiger statistics by their exact names, which are not a
    /// stable API and have moved between MongoDB releases. If they move again the probe
    /// silently reports no pressure and jobs run flat out - so this asserts against a
    /// real server rather than a fixture.
    /// </summary>
    [Test]
    public async Task ReadsTheServersActualStatistics()
    {
        var probe = new PressureProbe(MongoClient);

        var sample = await probe.Sample(CancellationToken.None);

        Assert.That(sample.DatabaseAvailable, Is.True,
            "serverStatus was readable but the WiredTiger cache statistics were not found - the field names have probably changed");
        Assert.That(sample.DirtyCacheFraction, Is.InRange(0.0, 1.0));
        Assert.That(sample.WriteTicketUtilisation, Is.InRange(0.0, 1.0));
        Assert.That(sample.ProcessorCount, Is.GreaterThan(0));
        Assert.That(sample.ProcessCpuTime, Is.GreaterThan(System.TimeSpan.Zero));
    }
}
