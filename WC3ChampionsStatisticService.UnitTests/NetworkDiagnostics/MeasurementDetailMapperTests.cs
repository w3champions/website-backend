using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using W3ChampionsStatisticService.NetworkDiagnostics;
using W3ChampionsStatisticService.NetworkDiagnostics.Results;
using W3ChampionsStatisticService.NetworkDiagnostics.Runs;
using W3ChampionsStatisticService.NetworkDiagnostics.Submission;

namespace WC3ChampionsStatisticService.Tests.NetworkDiagnostics;

[TestFixture]
public class MeasurementDetailMapperTests
{
    private static readonly DateTime Now = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

    private static BuiltResult Built(string json = SubmissionFixtures.FullJson) => ResultBuilder.Build(
        SubmissionFixtures.Parse(json),
        new NetworkDiagnosticAssignment { Id = SubmissionFixtures.MeasurementId, RunId = "run-1", BattleTag = "Name#1234" },
        Now, "81.2.69.160", new Dictionary<string, ClientGeo> { ["81.2.69.160"] = new() { Country = "GB" } });

    private static readonly List<NetworkDiagnosticIpInfo> IpInfo =
        [new() { RunId = "run-1", Ip = "1.2.3.4", Asn = 13335, AsnOrg = "Cloudflare", Country = "US", City = "Ashburn", Lat = 39.0, Lon = -77.5, GeoDbBuild = "2026-10-07" }];

    private static string Json(object o) => JsonSerializer.Serialize(o, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    [Test]
    public void Summary_HasTheC6Fields()
    {
        var s = MeasurementDetailMapper.Summary(Built().Measurement);

        Assert.AreEqual(SubmissionFixtures.MeasurementId, s.MeasurementId);
        Assert.AreEqual("run-1", s.RunId);
        Assert.AreEqual("Name#1234", s.BattleTag);
        Assert.AreEqual(Now, s.SubmittedAt);
        Assert.AreEqual("81.2.69.160", s.ClientIp);
        Assert.AreEqual("GB", s.ClientGeo.Country);
        Assert.AreEqual("windows", s.Os);
        Assert.AreEqual("ethernet", s.PrimaryAdapterType);
        Assert.IsTrue(s.VpnSuspected);
        Assert.AreEqual(1, s.TargetCount);
        Assert.AreEqual(1, s.Restarts);
    }

    // C6: exactly these keys, camelCase.
    [Test]
    public void Summary_SerializesTheC6Keys()
    {
        using var doc = JsonDocument.Parse(Json(MeasurementDetailMapper.Summary(Built().Measurement)));

        Assert.AreEqual(
            new[] { "measurementId", "runId", "battleTag", "submittedAt", "clientIp", "clientGeo", "os", "primaryAdapterType", "vpnSuspected", "targetCount", "restarts" },
            doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.AreEqual("2026-10-12T12:00:00Z", doc.RootElement.GetProperty("submittedAt").GetString());
    }

    // The launcher's unsigned 32-bit restart counter is stored as long (P10) and must not be narrowed on the way out.
    [Test]
    public void Summary_RestartsAboveIntMaxValue_AreKept() =>
        Assert.AreEqual(4294967295L, MeasurementDetailMapper.Summary(Built(SubmissionFixtures.LauncherProducedJson).Measurement).Restarts);

    [Test]
    public void Detail_WithoutRaw_OmitsSamples_AndMapsIpInfo()
    {
        var built = Built();

        var d = MeasurementDetailMapper.Detail(built.Measurement, built.RawSeries, IpInfo, includeRaw: false);
        var json = Json(d);

        var info = d.IpInfo["1.2.3.4"];
        Assert.AreEqual(13335, info.Asn);
        Assert.AreEqual(("Cloudflare", "US", "Ashburn", "2026-10-07"), (info.AsnOrg, info.Country, info.City, info.GeoDbBuild));
        Assert.AreEqual(39.0, info.Lat);
        Assert.AreEqual(-77.5, info.Lon);
        Assert.AreEqual("1.6.20", d.LauncherVersion);
        Assert.AreEqual("Windows 11 Pro", d.OsInfo.Name);
        Assert.AreEqual("windows", d.Os);
        Assert.AreEqual("192.168.1.1", d.DefaultGateway);
        Assert.AreEqual(6000, d.Pacing.MtrRoundMs);
        Assert.AreEqual("AMD Ryzen 7", d.System.Cpu.Model);
        Assert.AreEqual(new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc), d.AttemptStartedAt);
        Assert.AreEqual(new DateTime(2026, 10, 12, 10, 0, 15, DateTimeKind.Utc), d.WindowStartedAt);
        Assert.IsNotNull(d.WindowEndedAt);
        // P3: floSnapshot[].samples is a C5 count and always there; the "no samples key" rule is about the targets.
        Assert.AreEqual(256, d.FloSnapshot.Single().Samples);
        StringAssert.Contains("\"samples\":256", json);
        StringAssert.DoesNotContain("\"samples\"", Json(d.Targets));
        StringAssert.DoesNotContain("\"rawExpired\"", json);
        StringAssert.Contains("\"p50Ms\":15", json);
    }

    [Test]
    public void Detail_Targets_KeepTheStoredStructure()
    {
        var built = Built();

        var d = MeasurementDetailMapper.Detail(built.Measurement, built.RawSeries, IpInfo, includeRaw: false);

        var target = d.Targets.Single();
        Assert.AreEqual(("flo:12", "US East 4"), (target.TargetKey, target.Label));
        var path = target.Paths.Single();
        Assert.AreEqual(("direct", (string)null, "1.2.3.4:3554"), (path.Path, path.RouteLabel, path.Endpoints.Tcp));
        Assert.AreEqual(new[] { "IcmpMtr", "TcpPing", "TcpMtr", "FloUdpPing" }, path.Probes.Select(p => p.Method).ToArray());
        Assert.AreEqual(("unsupported", "os_requires_admin"), (path.Probes[2].Outcome, path.Probes[2].Reason));
        var mtr = path.Probes[0];
        Assert.AreEqual(15.0, mtr.Stats.P50Ms);
        Assert.AreEqual(new[] { 1, 2, 3 }, mtr.Hops.Select(h => h.Ttl).ToArray());
        Assert.AreEqual("1.128.0.1", mtr.Hops[1].Ip);
        Assert.AreEqual(50.0, mtr.Hops[0].Stats.LossPct);
        Assert.IsEmpty(path.Probes[1].Hops);
    }

    [Test]
    public void Detail_JsonKeys_AreTheC6CamelCaseNames()
    {
        var built = Built();

        using var doc = JsonDocument.Parse(Json(MeasurementDetailMapper.Detail(built.Measurement, built.RawSeries, IpInfo, includeRaw: true)));
        var root = doc.RootElement;

        Assert.AreEqual(
            new[]
            {
                "measurementId", "runId", "battleTag", "submittedAt", "clientIp", "clientGeo", "os", "primaryAdapterType", "vpnSuspected",
                "targetCount", "restarts", "osInfo", "launcherVersion", "attemptStartedAt", "windowStartedAt", "windowEndedAt", "pacing",
                "system", "floSnapshot", "defaultGateway", "ipInfo", "targets",
            },
            root.EnumerateObject().Select(p => p.Name).ToArray());
        CollectionAssert.AreEquivalent(new[] { "asn", "asnOrg", "country", "city", "lat", "lon", "geoDbBuild" },
            root.GetProperty("ipInfo").GetProperty("1.2.3.4").EnumerateObject().Select(p => p.Name).ToArray());
        // The C5 leaf records are written camelCase too.
        Assert.AreEqual(6000, root.GetProperty("pacing").GetProperty("mtrRoundMs").GetInt64());
        Assert.AreEqual("US East 4", root.GetProperty("floSnapshot")[0].GetProperty("nodeName").GetString());
        Assert.AreEqual(12.5, root.GetProperty("system").GetProperty("cpu").GetProperty("usagePct").GetDouble());
        var probe = root.GetProperty("targets")[0].GetProperty("paths")[0].GetProperty("probes")[0];
        Assert.AreEqual(new[] { "method", "outcome", "reason", "stats", "hops" }, probe.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.AreEqual(new[] { "ttl", "ip", "stats", "samples" }, probe.GetProperty("hops")[0].EnumerateObject().Select(p => p.Name).ToArray());
        Assert.AreEqual("[[0,1.234],[6000,null]]", probe.GetProperty("hops")[0].GetProperty("samples").GetRawText());
    }

    [Test]
    public void Detail_WithRaw_RestoresSamplesInMilliseconds()
    {
        var built = Built();

        var d = MeasurementDetailMapper.Detail(built.Measurement, built.RawSeries, IpInfo, includeRaw: true);

        var probes = d.Targets[0].Paths[0].Probes;
        Assert.AreEqual(new double?[] { 0, 15.0 }, probes[0].Hops[2].Samples[0]);
        Assert.AreEqual(new double?[] { 6000, 17.0 }, probes[0].Hops[2].Samples[1]);
        Assert.AreEqual(new double?[] { 1000, null }, probes[1].Samples[1]);
        Assert.AreEqual(15.234, probes[1].Samples[0][1]);
        Assert.IsNull(probes[0].Samples, "an MTR probe has its series on the hops");
        Assert.IsNull(probes[2].Samples, "an unsupported probe has no series");
        Assert.IsNull(probes[3].Samples, "a probe submitted without samples has no series");
        Assert.IsNull(d.RawExpired);
    }

    // A4: with raw=true every MTR/traceroute hop carries its own [[offsetMs, rttMs|null]] series.
    [Test]
    public void Detail_WithRaw_EveryHopCarriesItsOwnSeries()
    {
        var built = Built();

        var d = MeasurementDetailMapper.Detail(built.Measurement, built.RawSeries, IpInfo, includeRaw: true);

        var hops = d.Targets[0].Paths[0].Probes[0].Hops;
        var entries = built.RawSeries.Series.Where(e => e.ProbeIndex == 0).ToList();
        Assert.AreEqual(3, entries.Count);
        foreach (var entry in entries)
        {
            var expected = RawSeriesCodec.Decode(entry.Data)
                .Select(x => new double?[] { x.OffsetMs, x.RttUs is { } us ? us / 1000.0 : null }).ToList();
            Assert.AreEqual(expected, hops[entry.HopIndex!.Value].Samples, $"hop {entry.HopIndex}");
        }
    }

    // Series are addressed by (target, path, probe, hop) index: a second target and a second path get their own.
    [Test]
    public void Detail_WithRaw_SeriesOfLaterTargetsAndPaths_AreNotMixedUp()
    {
        var built = Built(SubmissionFixtures.LauncherProducedJson);

        var d = MeasurementDetailMapper.Detail(built.Measurement, built.RawSeries, [], includeRaw: true);

        Assert.AreEqual(new double?[] { 0, 1.234 }, d.Targets[0].Paths[0].Probes[0].Hops[0].Samples[0]);
        Assert.IsNull(d.Targets[0].Paths[0].Probes[1].Samples, "empty samples were submitted, so nothing was stored");
        var udp = d.Targets[1].Paths[0].Probes[1];
        Assert.AreEqual(new double?[] { 0, 15.234 }, udp.Samples[0]);
        Assert.AreEqual(new double?[] { 4294967295, 4294967.294 }, udp.Samples[1]);
        Assert.IsEmpty(d.IpInfo);
    }

    // A4: a hop that was submitted without samples still carries the key while the raw data exists.
    [Test]
    public void Detail_WithRaw_HopWithoutStoredSeries_HasAnEmptySeries()
    {
        var built = Built();
        var withoutHop1 = built.RawSeries with { Series = built.RawSeries.Series.Where(e => !(e.ProbeIndex == 0 && e.HopIndex == 1)).ToList() };

        var d = MeasurementDetailMapper.Detail(built.Measurement, withoutHop1, IpInfo, includeRaw: true);

        var hops = d.Targets[0].Paths[0].Probes[0].Hops;
        Assert.IsEmpty(hops[1].Samples);
        Assert.AreEqual(2, hops[0].Samples.Count);
        Assert.IsNull(d.RawExpired);
    }

    [Test]
    public void Detail_WithRaw_AfterRawExpired_FlagsIt()
    {
        var d = MeasurementDetailMapper.Detail(Built().Measurement, raw: null, IpInfo, includeRaw: true);

        Assert.AreEqual(true, d.RawExpired);
        Assert.IsTrue(d.Targets[0].Paths[0].Probes.All(p => p.Samples == null && p.Hops.All(h => h.Samples == null)));
        StringAssert.DoesNotContain("\"samples\"", Json(d.Targets), "A4: expired samples are omitted, never null");
        StringAssert.Contains("\"rawExpired\":true", Json(d));
    }

    // raw=false never looks at the raw series, so it cannot claim that they expired.
    [Test]
    public void Detail_WithoutRaw_NeverReportsRawExpired() =>
        Assert.IsNull(MeasurementDetailMapper.Detail(Built().Measurement, raw: null, IpInfo, includeRaw: false).RawExpired);

    [Test]
    public void IpsOf_IsTheClientAndEveryHopIp_OnceEach()
    {
        var ips = MeasurementDetailMapper.IpsOf(Built().Measurement);

        Assert.AreEqual(new[] { "81.2.69.160", "192.168.1.1", "1.128.0.1", "1.2.3.4" }, ips.ToArray());
    }

    [Test]
    public void IpsOf_SkipsMissingClientAndHopIps()
    {
        var m = new NetworkDiagnosticMeasurement
        {
            Targets = [new MeasuredTarget { Paths = [new MeasuredPath { Probes = [new MeasuredProbe { Hops = [new MeasuredHop { Ttl = 1 }, new MeasuredHop { Ttl = 2, Ip = "9.9.9.9" }, new MeasuredHop { Ttl = 3, Ip = "9.9.9.9" }] }] }] }],
        };

        Assert.AreEqual(new[] { "9.9.9.9" }, MeasurementDetailMapper.IpsOf(m).ToArray());
    }

    // Documents read back from Mongo can lack whole sub-documents (A12: every optional part may be absent).
    [Test]
    public void Detail_MeasurementWithoutOptionalParts_Maps()
    {
        var m = new NetworkDiagnosticMeasurement { Id = "m1", RunId = "run-1", BattleTag = "Name#1234", SubmittedAt = Now, Targets = null, FloSnapshot = null };

        var d = MeasurementDetailMapper.Detail(m, raw: null, [], includeRaw: false);

        Assert.IsEmpty(d.Targets);
        Assert.IsEmpty(d.FloSnapshot);
        Assert.IsNull(d.ClientGeo);
        Assert.IsNull(d.OsInfo);
        Assert.IsEmpty(MeasurementDetailMapper.IpsOf(m));
    }
}
