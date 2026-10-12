using System.Net;
using System.Net.Sockets;

namespace W3ChampionsStatisticService.NetworkDiagnostics;

public static class NetDiagIp
{
    /// <summary>Dotted-quad IPv4 only. IPAddress.TryParse alone accepts "1" or "1.2" as IPv4.</summary>
    public static bool IsStrictIpv4(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Split('.').Length != 4) return false;
        return IPAddress.TryParse(value, out var address)
            && address.AddressFamily == AddressFamily.InterNetwork
            && address.ToString() == value;
    }

    /// <summary>
    /// The IPv4 address of a peer: an IPv4 address as it is, an IPv4-mapped IPv6 address (::ffff:a.b.c.d, a dual-stack
    /// socket's view of an IPv4 peer) unwrapped. Null for a real IPv6 peer, which has none: MapToIPv4 would invent one
    /// from its last four bytes.
    /// </summary>
    public static string Ipv4Of(IPAddress peer)
    {
        if (peer == null) return null;
        if (peer.AddressFamily == AddressFamily.InterNetwork) return peer.ToString();
        return peer.IsIPv4MappedToIPv6 ? peer.MapToIPv4().ToString() : null;
    }

    /// <summary>"1.2.3.4:3554" → "1.2.3.4"; a bare host is returned unchanged.</summary>
    public static string HostOf(string endpoint)
    {
        if (endpoint == null) return null;
        var colon = endpoint.LastIndexOf(':');
        return colon > 0 ? endpoint[..colon] : endpoint;
    }
}
