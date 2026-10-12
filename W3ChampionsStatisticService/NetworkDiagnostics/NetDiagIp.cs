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

    /// <summary>"1.2.3.4:3554" → "1.2.3.4"; a bare host is returned unchanged.</summary>
    public static string HostOf(string endpoint)
    {
        if (endpoint == null) return null;
        var colon = endpoint.LastIndexOf(':');
        return colon > 0 ? endpoint[..colon] : endpoint;
    }
}
