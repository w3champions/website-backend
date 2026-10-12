using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using W3ChampionsStatisticService.NetworkDiagnostics.Runs;

namespace WC3ChampionsStatisticService.Tests.NetworkDiagnostics;

[TestFixture]
public class NetDiagTargetsConfigTests
{
    private static Func<string, string> Env(Dictionary<string, string> values) => name => values.GetValueOrDefault(name);

    [Test]
    public void Defaults_WhenUnset()
    {
        var cfg = NetDiagTargetsConfig.FromEnvironment(Env(new()));

        Assert.IsEmpty(cfg.CoreServers);
        Assert.AreEqual("/geoip/GeoLite2-City.mmdb", cfg.GeoIpCityDbPath);
        Assert.AreEqual("/geoip/GeoLite2-ASN.mmdb", cfg.GeoIpAsnDbPath);
        Assert.AreEqual(new[] { "cloudflare", "google", "quad9", "alidns", "dnspod", "yandex" }, cfg.Baselines.Select(b => b.Key));
        var dnspod = cfg.Baselines.Single(b => b.Key == "dnspod");
        Assert.AreEqual("119.29.29.29", dnspod.Ip);
        Assert.AreEqual(53, dnspod.TcpPort);
        Assert.IsFalse(cfg.Baselines.Any(b => b.Ip == "114.114.114.114"), "114 does not answer ICMP (spec §7)");
    }

    [Test]
    public void ParsesJsonArrays()
    {
        var cfg = NetDiagTargetsConfig.FromEnvironment(Env(new()
        {
            ["NETDIAG_CORE_SERVERS"] = """[{"key":"bigboi","label":"bigboi","ip":"203.0.113.10","tcpPort":443}]""",
            ["NETDIAG_BASELINES"] = """[{"key":"cloudflare","label":"Cloudflare 1.1.1.1","region":"global","ip":"1.1.1.1","tcpPort":443}]""",
            ["GEOIP_CITY_DB_PATH"] = "/x/city.mmdb",
        }));

        Assert.AreEqual(new CoreServerConfig { Key = "bigboi", Label = "bigboi", Ip = "203.0.113.10", TcpPort = 443 }, cfg.CoreServers.Single());
        Assert.AreEqual("global", cfg.Baselines.Single().Region);
        Assert.AreEqual("/x/city.mmdb", cfg.GeoIpCityDbPath);
    }

    [TestCase("""not json""")]
    [TestCase("""[{"key":"","label":"x","ip":"1.1.1.1","tcpPort":443}]""")]
    [TestCase("""[{"key":"a","label":"x","ip":"1.1.1","tcpPort":443}]""")]
    [TestCase("""[{"key":"a","label":"x","ip":"1.1.1.1","tcpPort":0}]""")]
    [TestCase("""[{"key":"a","label":"x","ip":"1.1.1.1","tcpPort":443},{"key":"a","label":"y","ip":"2.2.2.2","tcpPort":443}]""")]
    public void InvalidCoreServers_FailFast(string json) =>
        Assert.Throws<InvalidOperationException>(() => NetDiagTargetsConfig.FromEnvironment(Env(new() { ["NETDIAG_CORE_SERVERS"] = json })));

    [TestCase("""not json""")]
    [TestCase("""[null]""")]
    [TestCase("""{"key":"a"}""")]
    [TestCase("""[{"key":"a","label":"x","region":"r","ip":"::1","tcpPort":443}]""")]
    public void InvalidBaselines_FailFast(string json) =>
        Assert.Throws<InvalidOperationException>(() => NetDiagTargetsConfig.FromEnvironment(Env(new() { ["NETDIAG_BASELINES"] = json })));

    [Test]
    public void ResolveEndpoints_BuildsC4KeysAndLabels()
    {
        var cfg = NetDiagTargetsConfig.FromEnvironment(Env(new()
        {
            ["NETDIAG_CORE_SERVERS"] = """[{"key":"bigboi","label":"bigboi","ip":"203.0.113.10","tcpPort":443}]""",
        }));
        var targets = new RunTargets
        {
            CoreServers = ["bigboi"],
            Baselines = ["cloudflare"],
            CustomTargets = [new CustomTarget { Ip = "10.0.0.5", TcpPort = 443 }],
        };

        var endpoints = cfg.ResolveEndpoints(targets);

        Assert.AreEqual(new[]
        {
            new RunEndpoint { TargetKey = "core:bigboi", Label = "bigboi", Ip = "203.0.113.10", TcpPort = 443 },
            new RunEndpoint { TargetKey = "baseline:cloudflare", Label = "Cloudflare 1.1.1.1", Ip = "1.1.1.1", TcpPort = 443 },
            new RunEndpoint { TargetKey = "custom:10.0.0.5:443", Label = "10.0.0.5:443", Ip = "10.0.0.5", TcpPort = 443 },
        }, endpoints);
    }

    // The launcher drops a whole push when an endpoint's label is null, so a missing or blank label becomes the key.
    [TestCase("""[{"key":"bigboi","ip":"203.0.113.10","tcpPort":443}]""")]
    [TestCase("""[{"key":"bigboi","label":null,"ip":"203.0.113.10","tcpPort":443}]""")]
    [TestCase("""[{"key":"bigboi","label":"  ","ip":"203.0.113.10","tcpPort":443}]""")]
    public void MissingLabel_FallsBackToTheKey(string json)
    {
        var cfg = NetDiagTargetsConfig.FromEnvironment(Env(new() { ["NETDIAG_CORE_SERVERS"] = json, ["NETDIAG_BASELINES"] = json }));

        Assert.AreEqual("bigboi", cfg.CoreServers.Single().Label);
        Assert.AreEqual("bigboi", cfg.Baselines.Single().Label);
        Assert.AreEqual(new[] { "bigboi", "bigboi" },
            cfg.ResolveEndpoints(new RunTargets { CoreServers = ["bigboi"], Baselines = ["bigboi"] }).Select(e => e.Label));
    }

    // The rules hold for every instance, not only for one read from the environment: runs freeze these values and
    // push them to launchers, which reject a port outside 1-65535.
    [Test]
    public void Constructor_ValidatesLikeFromEnvironment()
    {
        CoreServerConfig Core(string key = "a", string ip = "1.1.1.1", int port = 443) => new() { Key = key, Ip = ip, TcpPort = port };
        NetDiagTargetsConfig Build(IReadOnlyList<CoreServerConfig> core, IReadOnlyList<BaselineConfig> baselines) => new(core, baselines, "c", "a");

        Assert.Throws<InvalidOperationException>(() => Build([Core(port: 0)], []));
        Assert.Throws<InvalidOperationException>(() => Build([Core(port: 65536)], []));
        Assert.Throws<InvalidOperationException>(() => Build([Core(ip: "1.1.1")], []));
        Assert.Throws<InvalidOperationException>(() => Build([Core(key: " ")], []));
        Assert.Throws<InvalidOperationException>(() => Build([Core(), Core()], []));
        Assert.Throws<InvalidOperationException>(() => Build([null], []));
        Assert.Throws<InvalidOperationException>(() => Build([], [new BaselineConfig { Key = "b", Ip = "1.1.1.1", TcpPort = 70000 }]));
        var ok = Build([Core(port: 1), Core(key: "b", port: 65535)], null);
        Assert.AreEqual(new[] { "a", "b" }, ok.CoreServers.Select(c => c.Label));
        Assert.IsEmpty(ok.Baselines);
        Assert.IsEmpty(Build(null, []).CoreServers);
    }

    [Test]
    public void UnknownKeys_AreReportedWithTheirKind()
    {
        var cfg = NetDiagTargetsConfig.FromEnvironment(Env(new()));
        Assert.AreEqual(new[] { "core:nope", "baseline:nada" },
            cfg.UnknownKeys(new RunTargets { CoreServers = ["nope"], Baselines = ["cloudflare", "nada"] }));
    }
}
