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
    /// The public IPv4 address of a peer, or null when it has none. An IPv4 address is taken as it is and an
    /// IPv4-mapped IPv6 address (::ffff:a.b.c.d, a dual-stack socket's view of an IPv4 peer) is unwrapped; a real IPv6
    /// peer has no IPv4 address (MapToIPv4 would invent one from its last four bytes). Addresses that cannot be a
    /// player's public address are dropped: they are a proxy or container hop, not the client.
    /// </summary>
    public static string PublicIpv4Of(IPAddress peer)
    {
        if (peer == null) return null;
        if (peer.IsIPv4MappedToIPv6) peer = peer.MapToIPv4();
        if (peer.AddressFamily != AddressFamily.InterNetwork) return null;
        var b = peer.GetAddressBytes();
        var notPublic = b[0] == 0                                   // unspecified, "this network"
            || b[0] == 10                                           // private 10/8
            || b[0] == 127                                          // loopback
            || (b[0] == 100 && (b[1] & 0xC0) == 64)                 // CGNAT 100.64/10
            || (b[0] == 169 && b[1] == 254)                         // link-local
            || (b[0] == 172 && (b[1] & 0xF0) == 16)                 // private 172.16/12
            || (b[0] == 192 && b[1] == 168);                        // private 192.168/16
        return notPublic ? null : peer.ToString();
    }

    /// <summary>"1.2.3.4:3554" → "1.2.3.4"; a bare host is returned unchanged.</summary>
    public static string HostOf(string endpoint)
    {
        if (endpoint == null) return null;
        var colon = endpoint.LastIndexOf(':');
        return colon > 0 ? endpoint[..colon] : endpoint;
    }
}
