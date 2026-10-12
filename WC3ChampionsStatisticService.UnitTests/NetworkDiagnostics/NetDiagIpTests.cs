using System.Net;
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

    // P5: only an IPv4 peer (plain or IPv4-mapped IPv6) has an IPv4 address; a real IPv6 peer has none.
    [TestCase("198.51.100.7", "198.51.100.7")]
    [TestCase("::ffff:198.51.100.7", "198.51.100.7")]
    [TestCase("2001:db8::1", null)]
    [TestCase("::1", null)]
    [TestCase("fe80::1%3", null)]
    [TestCase(null, null)]
    public void Ipv4Of(string peer, string expected) =>
        Assert.AreEqual(expected, NetDiagIp.Ipv4Of(peer == null ? null : IPAddress.Parse(peer)));
}
