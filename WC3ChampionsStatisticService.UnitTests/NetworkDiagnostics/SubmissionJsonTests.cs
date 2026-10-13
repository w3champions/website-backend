using System;
using System.Text.Json;
using NUnit.Framework;
using W3ChampionsStatisticService.NetworkDiagnostics.Submission;

namespace WC3ChampionsStatisticService.Tests.NetworkDiagnostics;

[TestFixture]
public class SubmissionJsonTests
{
    [Test]
    public void FullC5Payload_BindsEveryField()
    {
        var d = SubmissionFixtures.Parse(SubmissionFixtures.FullJson);

        // One assert per field (no tuples: nullable vs non-nullable tuple types never compare equal).
        Assert.AreEqual(SubmissionFixtures.MeasurementId, d.MeasurementId);
        Assert.AreEqual("1.6.20", d.LauncherVersion);
        Assert.AreEqual("windows", d.Os.Family);
        Assert.AreEqual("Windows 11 Pro", d.Os.Name);
        Assert.AreEqual("10.0.26200", d.Os.Version);
        Assert.AreEqual(new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc), d.AttemptStartedAt);
        Assert.AreEqual(new DateTime(2026, 10, 12, 10, 0, 15, DateTimeKind.Utc), d.WindowStartedAt);
        // 9 fraction digits (chrono AutoSi): .NET keeps 7.
        Assert.AreEqual(new DateTime(2026, 10, 12, 10, 1, 15, DateTimeKind.Utc).AddTicks(1234567), d.WindowEndedAt);
        Assert.AreEqual(DateTimeKind.Utc, d.WindowEndedAt.Value.Kind);
        Assert.AreEqual(1, d.Restarts);
        Assert.AreEqual("81.2.69.160", d.PublicIpv4);
        Assert.AreEqual("192.168.1.1", d.DefaultGateway);
        Assert.AreEqual(6000, d.Pacing.MtrRoundMs);
        Assert.AreEqual(1000, d.Pacing.PingIntervalMs);
        Assert.AreEqual(330, d.Pacing.Streams);
        Assert.AreEqual(2000, d.Pacing.IcmpSendGapUs);

        var s = d.System;
        Assert.AreEqual("AMD Ryzen 7", s.Cpu.Model);
        Assert.AreEqual(8.0, s.Cpu.Cores);
        Assert.AreEqual(12.5, s.Cpu.UsagePct);
        Assert.AreEqual(32768.0, s.Memory.TotalMb);
        Assert.AreEqual(14000.0, s.Memory.UsedMb);
        Assert.AreEqual(123456.0, s.UptimeS);
        Assert.AreEqual(12.0, s.ClockSkewMs);
        Assert.AreEqual(false, s.LauncherProxyConfigured);
        var a = s.Adapters[0];
        Assert.AreEqual("Ethernet 2", a.Name);
        Assert.AreEqual("ethernet", a.Kind);
        Assert.AreEqual(1000.0, a.LinkSpeedMbps);
        Assert.AreEqual(1500, a.Mtu);
        Assert.AreEqual("192.168.1.1", a.Gateway);
        Assert.AreEqual(new[] { "192.168.1.1" }, a.Dns);
        Assert.AreEqual(true, a.HasDefaultRoute);
        Assert.AreEqual(12000.0, a.RxBps);
        Assert.AreEqual(3000.0, a.TxBps);
        Assert.AreEqual("2026-10-12T08:00:00Z", a.UpSince);
        Assert.IsNull(a.UpSinceReason);
        Assert.AreEqual(-55.0, s.Wifi.RssiDbm);
        Assert.AreEqual(-90.0, s.Wifi.NoiseDbm);
        Assert.AreEqual(36, s.Wifi.Channel);
        Assert.AreEqual("5GHz", s.Wifi.Band);
        Assert.AreEqual(573.0, s.Wifi.TxRateMbps);
        Assert.AreEqual(true, s.Vpn.Suspected);
        Assert.AreEqual(new[] { "utun3 holds default route" }, s.Vpn.Evidence);
        Assert.AreEqual(new[] { "Kaspersky" }, s.Antivirus.ThirdParty);

        var f = d.FloSnapshot[0];
        Assert.AreEqual(12, f.NodeId);
        Assert.AreEqual("US East 4", f.NodeName);
        Assert.AreEqual("US East 4 -> US East 3", f.RouteLabel);
        Assert.AreEqual("QUIC", f.Transport);
        Assert.AreEqual("ok", f.Health);
        Assert.AreEqual("connected", f.ControllerStatus);
        Assert.AreEqual(10.0, f.Min);
        Assert.AreEqual(15.0, f.Max);
        Assert.AreEqual(12.0, f.Avg);
        Assert.AreEqual(0.8, f.Stddev);
        Assert.AreEqual(12.0, f.Current);
        Assert.AreEqual(0.0, f.LossRate);
        Assert.AreEqual(12.0, f.P50);
        Assert.AreEqual(14.0, f.P90);
        Assert.AreEqual(16.0, f.P99);
        Assert.AreEqual(40.0, f.MaxTrue);
        Assert.AreEqual(256, f.Samples);
        Assert.AreEqual(3800000.0, f.WindowMs);

        var t = d.Targets[0];
        Assert.AreEqual("flo:12", t.TargetKey);
        Assert.AreEqual("US East 4", t.Label);
        var p = t.Paths[0];
        Assert.AreEqual("direct", p.Path);
        Assert.IsNull(p.RouteLabel);
        Assert.AreEqual("1.2.3.4", p.Endpoints.Icmp);
        Assert.AreEqual("1.2.3.4:3554", p.Endpoints.Tcp);
        Assert.AreEqual("1.2.3.4:3552", p.Endpoints.Udp);
        var mtr = p.Probes[0];
        Assert.AreEqual("IcmpMtr", mtr.Method);
        Assert.AreEqual("ok", mtr.Outcome);
        Assert.IsNull(mtr.Reason);
        Assert.AreEqual(1, mtr.Hops[0].Ttl);
        Assert.AreEqual("192.168.1.1", mtr.Hops[0].Ip);
        Assert.AreEqual(new long?[] { 6000, null }, mtr.Hops[0].Samples[1]);
        Assert.AreEqual(new long?[] { 0, 15234 }, p.Probes[1].Samples[0]);
        Assert.AreEqual("os_requires_admin", p.Probes[2].Reason);
        Assert.IsNull(p.Probes[3].Hops);
        Assert.IsNull(p.Probes[3].Samples);
    }

    [Test]
    public void UnknownMethodAndOutcome_StayStrings_AndUnknownFieldsAreIgnored()
    {
        const string json = """
        { "measurement_id": "m", "future_field": 1,
          "targets": [{ "target_key": "gateway", "paths": [{ "path": "direct",
            "probes": [{ "method": "QuicPing", "outcome": "rate_limited", "samples": [] }] }] }] }
        """;

        var probe = SubmissionFixtures.Parse(json).Targets[0].Paths[0].Probes[0];

        Assert.AreEqual("QuicPing", probe.Method);
        Assert.AreEqual("rate_limited", probe.Outcome);
    }

    [TestCase("""{ "pacing": { "streams": 1 } }""")]
    [TestCase("""{ "pacing": { "streams": 1, "icmp_send_gap_us": null } }""")]
    public void IcmpSendGapUs_AbsentOrNull_IsNull(string json) =>
        Assert.IsNull(SubmissionFixtures.Parse(json).Pacing.IcmpSendGapUs);

    // A12: unknown properties at every nesting level are ignored.
    [Test]
    public void UnknownPropertiesAtEveryLevel_AreIgnored()
    {
        const string json = """
        { "measurement_id": "m", "x": {"a": [1]},
          "os": { "family": "macos", "x": 1 },
          "pacing": { "streams": 1, "x": 1 },
          "system": { "x": 1,
            "cpu": { "cores": 2, "x": 1 }, "memory": { "x": 1 },
            "adapters": [{ "name": "a", "x": 1 }],
            "wifi": { "x": 1 }, "vpn": { "x": 1 }, "antivirus": { "x": 1 } },
          "flo_snapshot": [{ "node_id": 1, "x": 1 }],
          "targets": [{ "target_key": "gateway", "x": 1, "paths": [{ "path": "direct", "x": 1,
            "endpoints": { "x": 1 },
            "probes": [{ "method": "TcpMtr", "x": 1, "hops": [{ "ttl": 1, "x": 1, "samples": [] }] }] }] }] }
        """;

        var d = SubmissionFixtures.Parse(json);

        Assert.AreEqual(1, d.Pacing.Streams);
        Assert.AreEqual(1, d.Targets[0].Paths[0].Probes[0].Hops[0].Ttl);
        Assert.IsNull(SubmissionValidator.ValidateSubmission(d, "m"));
    }

    // A12: every optional sub-object and field may be an explicit null.
    [Test]
    public void AllOptionalsExplicitNull_ParseAndValidate()
    {
        const string json = """
        { "measurement_id": "m", "launcher_version": null, "os": null,
          "attempt_started_at": null, "window_started_at": null, "window_ended_at": null,
          "public_ipv4": null, "default_gateway": null, "pacing": null,
          "system": { "cpu": null, "memory": null, "uptime_s": null, "clock_skew_ms": null,
            "launcher_proxy_configured": null, "adapters": null, "wifi": null, "vpn": null, "antivirus": null },
          "flo_snapshot": null,
          "targets": [{ "target_key": "gateway", "label": null, "paths": [{ "path": "direct", "route_label": null,
            "endpoints": { "icmp": null, "tcp": null, "udp": null },
            "probes": [{ "method": "IcmpMtr", "outcome": "ok", "reason": null, "hops": null, "samples": null }] }] },
            { "target_key": "x", "paths": [{ "path": "direct", "endpoints": null, "probes": null }] },
            { "target_key": "y", "paths": null }] }
        """;

        var d = SubmissionFixtures.Parse(json);

        Assert.IsNull(d.Os);
        Assert.IsNull(d.System.Cpu);
        Assert.IsNull(d.Targets[0].Paths[0].Endpoints.Icmp);
        Assert.IsNull(d.Targets[1].Paths[0].Endpoints);
        Assert.IsNull(SubmissionValidator.ValidateSubmission(d, "m"));
    }

    // A12: every optional key may be absent altogether.
    [Test]
    public void AllOptionalsAbsent_ParseAndValidate()
    {
        const string json = """
        { "measurement_id": "m",
          "system": {},
          "targets": [{ "target_key": "gateway", "paths": [{ "path": "direct",
            "probes": [{ "method": "IcmpMtr", "outcome": "ok" }, { "method": "TcpPing", "outcome": "ok" }] }] }] }
        """;

        var d = SubmissionFixtures.Parse(json);

        Assert.IsNull(d.Os);
        Assert.IsNull(d.Pacing);
        Assert.IsNull(d.FloSnapshot);
        Assert.IsNull(d.Targets[0].Paths[0].Endpoints);
        Assert.IsNull(d.Targets[0].Paths[0].Probes[0].Hops);
        Assert.IsNull(d.Targets[0].Paths[0].Probes[1].Samples);
        Assert.IsNull(SubmissionValidator.ValidateSubmission(d, "m"));
    }

    [Test]
    public void LauncherProducedPayload_BindsAndValidates()
    {
        var d = SubmissionFixtures.Parse(SubmissionFixtures.LauncherProducedJson);

        Assert.AreEqual(4294967295L, d.Restarts);
        Assert.AreEqual(new DateTime(2026, 9, 21, 14, 13, 22, 250, DateTimeKind.Utc), d.WindowStartedAt);
        Assert.AreEqual(new DateTime(2026, 9, 21, 14, 14, 22, 250, DateTimeKind.Utc).AddTicks(1230), d.WindowEndedAt);
        Assert.IsNull(d.DefaultGateway);
        Assert.AreEqual(4294967295L, d.Pacing.Streams);
        Assert.AreEqual(4294967295L, d.System.Adapters[0].Mtu);
        Assert.AreEqual(4294967295L, d.System.Wifi.Channel);
        Assert.AreEqual(286.5, d.System.Wifi.TxRateMbps);
        Assert.AreEqual(4294967295L, d.FloSnapshot[0].Samples);
        Assert.AreEqual("controller_unreachable", d.FloSnapshot[0].Health);
        Assert.IsEmpty(d.System.Adapters[0].Dns);
        Assert.AreEqual("proxied", d.Targets[1].Paths[0].Path);
        Assert.IsEmpty(d.Targets[0].Paths[0].Probes[1].Samples);
        Assert.IsEmpty(d.Targets[1].Paths[0].Probes[0].Hops);
        Assert.IsNull(SubmissionValidator.ValidateSubmission(d, SubmissionFixtures.MeasurementId));
    }

    [Test]
    public void SnakeCaseRecords_WriteCamelCase_ForTheAdminApi()
    {
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(new NetDiagWifi { RssiDbm = -55, TxRateMbps = 573 }, web);
        StringAssert.Contains("\"rssiDbm\":-55", json);
        StringAssert.Contains("\"txRateMbps\":573", json);
    }
}
