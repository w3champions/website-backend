using NUnit.Framework;
using W3ChampionsStatisticService.NetworkDiagnostics;

namespace WC3ChampionsStatisticService.Tests.NetworkDiagnostics;

[TestFixture]
public class NetDiagIpTests
{
    [TestCase("1.2.3.4", true)]
    [TestCase("10.0.0.5", true)]
    [TestCase("255.255.255.255", true)]
    [TestCase("1", false)]           // IPAddress.TryParse accepts this as 0.0.0.1
    [TestCase("1.2.3", false)]
    [TestCase("1.2.3.256", false)]
    [TestCase("::1", false)]
    [TestCase(" 1.2.3.4", false)]
    [TestCase(null, false)]
    public void IsStrictIpv4(string input, bool expected) => Assert.AreEqual(expected, NetDiagIp.IsStrictIpv4(input));

    [TestCase("1.2.3.4:3554", "1.2.3.4")]
    [TestCase("1.2.3.4", "1.2.3.4")]
    [TestCase(null, null)]
    public void HostOf(string input, string expected) => Assert.AreEqual(expected, NetDiagIp.HostOf(input));
}
