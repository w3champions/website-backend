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

    // P5: only an IPv4 peer (plain or IPv4-mapped IPv6) has an IPv4 address; a real IPv6 peer has none. An address
    // that cannot be a player's public address (a proxy or container hop) is not a client IP either.
    [TestCase("198.51.100.7", "198.51.100.7")]
    [TestCase("::ffff:198.51.100.7", "198.51.100.7")]
    [TestCase("2001:db8::1", null)]
    [TestCase("2001:db8::c633:6407", null)]   // its last four bytes read as 198.51.100.7
    [TestCase("::1", null)]
    [TestCase("::", null)]
    [TestCase("fe80::1%3", null)]
    [TestCase(null, null)]
    [TestCase("0.0.0.0", null)]
    [TestCase("9.255.255.255", "9.255.255.255")]
    [TestCase("10.0.0.0", null)]
    [TestCase("10.255.255.255", null)]
    [TestCase("11.0.0.0", "11.0.0.0")]
    [TestCase("100.63.255.255", "100.63.255.255")]
    [TestCase("100.64.0.0", null)]
    [TestCase("100.127.255.255", null)]
    [TestCase("100.128.0.0", "100.128.0.0")]
    [TestCase("127.0.0.1", null)]
    [TestCase("127.255.255.255", null)]
    [TestCase("169.254.0.1", null)]
    [TestCase("169.253.0.1", "169.253.0.1")]
    [TestCase("172.15.255.255", "172.15.255.255")]
    [TestCase("172.16.0.0", null)]
    [TestCase("172.18.0.5", null)]
    [TestCase("172.31.255.255", null)]
    [TestCase("172.32.0.0", "172.32.0.0")]
    [TestCase("192.168.0.1", null)]
    [TestCase("192.169.0.1", "192.169.0.1")]
    [TestCase("::ffff:10.1.2.3", null)]
    [TestCase("::ffff:127.0.0.1", null)]
    public void PublicIpv4Of(string peer, string expected) =>
        Assert.AreEqual(expected, NetDiagIp.PublicIpv4Of(peer == null ? null : IPAddress.Parse(peer)));
}
