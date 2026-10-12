using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace W3ChampionsStatisticService.NetworkDiagnostics.Runs;

public record CoreServerConfig
{
    public string Key { get; init; }
    public string Label { get; init; }
    public string Ip { get; init; }
    public int TcpPort { get; init; }
}

public record BaselineConfig
{
    public string Key { get; init; }
    public string Label { get; init; }
    public string Region { get; init; }
    public string Ip { get; init; }
    public int TcpPort { get; init; }
}

/// <summary>C7 environment: target lists and GeoLite2 paths.</summary>
public sealed record NetDiagTargetsConfig(
    IReadOnlyList<CoreServerConfig> CoreServers,
    IReadOnlyList<BaselineConfig> Baselines,
    string GeoIpCityDbPath,
    string GeoIpAsnDbPath)
{
    public const string CoreServersVariable = "NETDIAG_CORE_SERVERS";
    public const string BaselinesVariable = "NETDIAG_BASELINES";
    public const string CityDbVariable = "GEOIP_CITY_DB_PATH";
    public const string AsnDbVariable = "GEOIP_ASN_DB_PATH";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Spec §7 initial set. 114.114.114.114 is excluded: it does not answer ICMP.</summary>
    public static readonly IReadOnlyList<BaselineConfig> DefaultBaselines =
    [
        new() { Key = "cloudflare", Label = "Cloudflare 1.1.1.1", Region = "global", Ip = "1.1.1.1", TcpPort = 443 },
        new() { Key = "google", Label = "Google 8.8.8.8", Region = "global", Ip = "8.8.8.8", TcpPort = 443 },
        new() { Key = "quad9", Label = "Quad9 9.9.9.9", Region = "global", Ip = "9.9.9.9", TcpPort = 443 },
        new() { Key = "alidns", Label = "AliDNS 223.5.5.5", Region = "cn", Ip = "223.5.5.5", TcpPort = 443 },
        new() { Key = "dnspod", Label = "DNSPod 119.29.29.29", Region = "cn", Ip = "119.29.29.29", TcpPort = 53 },
        new() { Key = "yandex", Label = "Yandex 77.88.8.8", Region = "ru", Ip = "77.88.8.8", TcpPort = 53 },
    ];

    public static NetDiagTargetsConfig FromEnvironment(Func<string, string> env)
    {
        var core = Parse<CoreServerConfig>(env(CoreServersVariable), CoreServersVariable) ?? [];
        var baselines = Parse<BaselineConfig>(env(BaselinesVariable), BaselinesVariable) ?? DefaultBaselines.ToList();
        Check(CoreServersVariable, core.Select(c => (c.Key, c.Ip, c.TcpPort)));
        Check(BaselinesVariable, baselines.Select(b => (b.Key, b.Ip, b.TcpPort)));
        return new NetDiagTargetsConfig(
            core,
            baselines,
            NonEmpty(env(CityDbVariable)) ?? "/geoip/GeoLite2-City.mmdb",
            NonEmpty(env(AsnDbVariable)) ?? "/geoip/GeoLite2-ASN.mmdb");
    }

    public List<string> UnknownKeys(RunTargets targets) =>
        (targets.CoreServers ?? []).Where(k => CoreServers.All(c => c.Key != k)).Select(k => $"core:{k}")
        .Concat((targets.Baselines ?? []).Where(k => Baselines.All(b => b.Key != k)).Select(k => $"baseline:{k}"))
        .ToList();

    /// <summary>Core, baseline and custom endpoints in that order. Call only after UnknownKeys is empty.</summary>
    public List<RunEndpoint> ResolveEndpoints(RunTargets targets) =>
        (targets.CoreServers ?? []).Select(k => CoreServers.First(c => c.Key == k))
            .Select(c => new RunEndpoint { TargetKey = $"core:{c.Key}", Label = c.Label, Ip = c.Ip, TcpPort = c.TcpPort })
        .Concat((targets.Baselines ?? []).Select(k => Baselines.First(b => b.Key == k))
            .Select(b => new RunEndpoint { TargetKey = $"baseline:{b.Key}", Label = b.Label, Ip = b.Ip, TcpPort = b.TcpPort }))
        .Concat((targets.CustomTargets ?? []).Select(c => (c.Ip, Port: c.TcpPort ?? NetDiagConstants.DefaultCustomTcpPort))
            .Select(c => new RunEndpoint { TargetKey = $"custom:{c.Ip}:{c.Port}", Label = $"{c.Ip}:{c.Port}", Ip = c.Ip, TcpPort = c.Port }))
        .ToList();

    private static List<T> Parse<T>(string raw, string variable)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            var list = JsonSerializer.Deserialize<List<T>>(raw, Json) ?? [];
            if (list.Contains(default)) throw new InvalidOperationException($"{variable} contains a null entry");
            return list;
        }
        catch (JsonException e)
        {
            throw new InvalidOperationException($"{variable} is not a valid JSON array: {e.Message}", e);
        }
    }

    private static void Check(string variable, IEnumerable<(string Key, string Ip, int Port)> entries)
    {
        var list = entries.ToList();
        foreach (var (key, ip, port) in list)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException($"{variable}: every entry needs a key");
            if (!NetDiagIp.IsStrictIpv4(ip)) throw new InvalidOperationException($"{variable}: '{key}' has an invalid IPv4 '{ip}'");
            if (port is < 1 or > 65535) throw new InvalidOperationException($"{variable}: '{key}' has an invalid tcpPort {port}");
        }
        var duplicate = list.GroupBy(e => e.Key).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null) throw new InvalidOperationException($"{variable}: duplicate key '{duplicate.Key}'");
    }

    private static string NonEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
