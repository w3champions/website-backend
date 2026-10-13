using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using W3ChampionsStatisticService.NetworkDiagnostics;
using W3ChampionsStatisticService.NetworkDiagnostics.Results;
using W3ChampionsStatisticService.NetworkDiagnostics.Runs;
using W3ChampionsStatisticService.NetworkDiagnostics.Submission;

namespace WC3ChampionsStatisticService.Tests.NetworkDiagnostics;

[TestFixture]
public class ResultBuilderTests
{
    private static readonly DateTime SubmittedAt = new(2026, 10, 12, 10, 2, 0, DateTimeKind.Utc);

    private static readonly NetworkDiagnosticAssignment Assignment = new()
    {
        Id = SubmissionFixtures.MeasurementId,
        RunId = "run-1",
        BattleTag = "Name#1234",
    };

    private static readonly Dictionary<string, ClientGeo> Geo = new()
    {
        ["81.2.69.160"] = new ClientGeo { Country = "GB", City = "London", Lat = 51.5142, Lon = -0.0931, Asn = 20712, AsnOrg = "Andrews & Arnold", GeoDbBuild = "2026-10-07" },
        ["1.2.3.4"] = new ClientGeo { Country = "US", Asn = 13335, AsnOrg = "Cloudflare", GeoDbBuild = "2026-10-07" },
    };

    private static BuiltResult BuildFull() =>
        ResultBuilder.Build(SubmissionFixtures.Parse(SubmissionFixtures.FullJson), Assignment, SubmittedAt, "81.2.69.160", Geo);

    [Test]
    public void Measurement_CarriesClientDimensionsAndRetention()
    {
        var m = BuildFull().Measurement;

        Assert.AreEqual(SubmissionFixtures.MeasurementId, m.Id);
        Assert.AreEqual("run-1", m.RunId);
        Assert.AreEqual("Name#1234", m.BattleTag);
        Assert.AreEqual(SubmittedAt, m.SubmittedAt);
        Assert.AreEqual(SubmittedAt.AddDays(365), m.ExpiresAt);
        Assert.AreEqual("81.2.69.160", m.ClientIp);
        Assert.AreEqual("GB", m.ClientGeo.Country);
        Assert.AreEqual("windows", m.Os);
        Assert.AreEqual("Windows 11 Pro", m.OsInfo.Name);
        Assert.AreEqual("ethernet", m.PrimaryAdapterType);
        Assert.IsTrue(m.VpnSuspected);
        Assert.AreEqual(1, m.TargetCount);
        Assert.AreEqual(1, m.Restarts);
        Assert.AreEqual(6000, m.Pacing.MtrRoundMs);
        Assert.AreEqual(2000, m.Pacing.IcmpSendGapUs);
        Assert.AreEqual(256, m.FloSnapshot.Single().Samples);
    }

    // NodeId N and TargetKey "flo:N" are the same statement (the admin node filter relies on it): a key that only
    // parses as a number names no node.
    [TestCase("flo:12", 12)]
    [TestCase("flo:0", 0)]
    [TestCase("flo:-3", -3)]
    [TestCase("flo:012", null)]
    [TestCase("flo:+12", null)]
    [TestCase("flo: 12", null)]
    [TestCase("flo:12 ", null)]
    [TestCase("flo:1,2", null)]
    [TestCase("flo:", null)]
    [TestCase("FLO:12", null)]
    [TestCase("core:12", null)]
    [TestCase("gateway", null)]
    [TestCase(null, null)]
    public void Observation_NodeId_OnlyForTheCanonicalFloKey(string targetKey, int? expected)
    {
        var dto = SubmissionFixtures.Minimal(SubmissionFixtures.MeasurementId) with
        {
            Targets = [new NetDiagTargetDto { TargetKey = targetKey, Paths = [new NetDiagPathDto { Path = "direct", Probes = [new NetDiagProbeDto { Method = "TcpPing" }] }] }],
        };

        var observation = ResultBuilder.Build(dto, Assignment, SubmittedAt, null, Geo).Observations.Single();

        Assert.AreEqual(expected, observation.NodeId);
        if (expected is { } node) Assert.AreEqual(targetKey, NetDiagConstants.FloTargetKey(node));
    }

    [Test]
    public void Measurement_MtrProbeStatsComeFromTheTargetHop()
    {
        var probes = BuildFull().Measurement.Targets[0].Paths[0].Probes;
        var mtr = probes[0];

        Assert.AreEqual(3, mtr.Hops.Count);
        Assert.AreEqual(50.0, mtr.Hops[0].Stats.LossPct);
        Assert.AreEqual(15.0, mtr.Stats.P50Ms);     // hop 3 = 1.2.3.4: 15000 µs, 17000 µs
        Assert.AreEqual(16.0, mtr.Stats.AvgMs);

        var ping = probes[1];
        Assert.AreEqual(2, ping.Stats.Sent);
        Assert.AreEqual(1, ping.Stats.Received);
        Assert.AreEqual(15.234, ping.Stats.P50Ms);

        var unsupported = probes[2];
        Assert.AreEqual("unsupported", unsupported.Outcome);
        Assert.IsNull(unsupported.Stats, "nothing was sent, so there are no statistics");
    }

    [Test]
    public void Observations_OnePerProbe_WithFinalHopAndClientDimensions()
    {
        var obs = BuildFull().Observations;

        Assert.AreEqual(4, obs.Count); // the fixture's FloUdpPing probe has neither hops nor samples and still yields a row
        var mtr = obs.Single(o => o.Method == "IcmpMtr");
        Assert.AreEqual(SubmissionFixtures.MeasurementId, mtr.MeasurementId);
        Assert.AreEqual("run-1", mtr.RunId);
        Assert.AreEqual("flo:12", mtr.TargetKey);
        Assert.AreEqual(12, mtr.NodeId);
        Assert.AreEqual("US East 4", mtr.TargetLabel);
        Assert.AreEqual("1.2.3.4", mtr.TargetIp);
        Assert.AreEqual("direct", mtr.Path);
        Assert.AreEqual("ok", mtr.Outcome);
        Assert.AreEqual(3, mtr.HopCount);
        Assert.AreEqual("1.2.3.4", mtr.LastHopIp);
        Assert.AreEqual(13335, mtr.LastHopAsn);
        Assert.AreEqual(15.0, mtr.P50Ms);
        Assert.AreEqual("GB", mtr.Country);
        Assert.AreEqual(20712, mtr.Asn);
        Assert.AreEqual(51.5142, mtr.Lat);
        Assert.AreEqual("windows", mtr.Os);
        Assert.AreEqual("ethernet", mtr.PrimaryAdapterType);
        Assert.IsTrue(mtr.VpnSuspected);
        Assert.AreEqual(SubmittedAt.AddDays(365), mtr.ExpiresAt);

        var ping = obs.Single(o => o.Method == "TcpPing");
        Assert.AreEqual("1.2.3.4", ping.TargetIp);   // host of endpoints.tcp
        Assert.AreEqual(50.0, ping.LossPct);
        Assert.AreEqual(0, ping.HopCount);
        Assert.IsNull(ping.LastHopIp);

        var noData = obs.Single(o => o.Method == "FloUdpPing");
        Assert.AreEqual("1.2.3.4", noData.TargetIp);
        Assert.AreEqual(0, noData.Sent);
        Assert.IsNull(noData.LossPct);

        var unsupported = obs.Single(o => o.Method == "TcpMtr");
        Assert.AreEqual("os_requires_admin", unsupported.Reason);
        Assert.AreEqual(0, unsupported.Sent);
        Assert.IsNull(unsupported.LossPct);
    }

    [Test]
    public void RawSeries_OneEntryPerSeriesAndHop_DecodesBack()
    {
        var raw = BuildFull().RawSeries;

        Assert.AreEqual(SubmissionFixtures.MeasurementId, raw.Id);
        Assert.AreEqual(SubmittedAt.AddDays(90), raw.ExpiresAt);
        Assert.AreEqual(4, raw.Series.Count);
        Assert.AreEqual(new int?[] { 0, 1, 2 }, raw.Series.Where(s => s.ProbeIndex == 0).Select(s => s.HopIndex));
        var ping = raw.Series.Single(s => s.ProbeIndex == 1);
        Assert.IsNull(ping.HopIndex);
        Assert.AreEqual(new[] { new ProbeSample(0, 15234), new ProbeSample(1000, null) }, RawSeriesCodec.Decode(ping.Data));
    }

    // A4: per-hop series stay addressable by (target, path, probe, hop), so the detail endpoint can return hop samples.
    [Test]
    public void RawSeries_HopEntries_AreUniquelyAddressable_AndMatchTheSubmittedHopSamples()
    {
        var submission = SubmissionFixtures.Parse(SubmissionFixtures.FullJson);
        var raw = BuildFull().RawSeries;

        Assert.AreEqual(raw.Series.Count, raw.Series.Select(e => (e.TargetIndex, e.PathIndex, e.ProbeIndex, e.HopIndex)).Distinct().Count());
        var hops = submission.Targets[0].Paths[0].Probes[0].Hops;
        foreach (var entry in raw.Series.Where(e => e.HopIndex != null))
        {
            Assert.AreEqual(SubmissionValidator.ToSamples(hops[entry.HopIndex!.Value].Samples), RawSeriesCodec.Decode(entry.Data), $"hop {entry.HopIndex}");
        }
    }

    [Test]
    public void FinalHop_FallsBackToLastAnsweringHop_WhenTargetNeverAnswers()
    {
        var probe = new NetDiagProbeDto
        {
            Method = "IcmpMtr",
            Outcome = "ok",
            Hops =
            [
                new NetDiagHopDto { Ttl = 1, Ip = "192.168.1.1", Samples = [new long?[] { 0, 1000 }] },
                new NetDiagHopDto { Ttl = 2, Ip = "10.1.1.1", Samples = [new long?[] { 0, 5000 }] },
                new NetDiagHopDto { Ttl = 3, Ip = null, Samples = [new long?[] { 0, null }] },
            ],
        };
        var obs = BuildSingle(probe).Observations.Single();

        Assert.AreEqual("10.1.1.1", obs.LastHopIp);
        Assert.AreEqual(5.0, obs.P50Ms);
    }

    [Test]
    public void FinalHop_IsTheUnansweredTargetHop_WhenTheTargetWasSeenButNeverAnswered()
    {
        var probe = new NetDiagProbeDto
        {
            Method = "IcmpMtr",
            Outcome = "ok",
            Hops =
            [
                new NetDiagHopDto { Ttl = 1, Ip = "192.168.1.1", Samples = Series(60, answered: 60, rttUs: 1000) },
                new NetDiagHopDto { Ttl = 2, Ip = "10.1.1.1", Samples = Series(60, answered: 60, rttUs: 5000) },
                new NetDiagHopDto { Ttl = 3, Ip = "9.9.9.9", Samples = Series(60, answered: 0, rttUs: 0) },
            ],
        };
        var built = BuildSingle(probe);
        var obs = built.Observations.Single();

        Assert.AreEqual((60, 0, 100.0), (obs.Sent, obs.Received, obs.LossPct));
        Assert.IsNull(obs.LastHopIp, "the hop did not answer");
        Assert.AreEqual((60, 0), (built.Measurement.Targets[0].Paths[0].Probes[0].Stats.Sent, built.Measurement.Targets[0].Paths[0].Probes[0].Stats.Received));
    }

    [Test]
    public void FinalHop_AnsweredTargetHop_WinsOverAnUnansweredOneAtAnotherTtl()
    {
        var probe = new NetDiagProbeDto
        {
            Method = "IcmpMtr",
            Outcome = "ok",
            Hops =
            [
                new NetDiagHopDto { Ttl = 4, Ip = "9.9.9.9", Samples = Series(60, answered: 0, rttUs: 0) },
                new NetDiagHopDto { Ttl = 3, Ip = "9.9.9.9", Samples = Series(60, answered: 30, rttUs: 7000) },
            ],
        };
        var obs = BuildSingle(probe).Observations.Single();

        Assert.AreEqual((60, 30), (obs.Sent, obs.Received));
        Assert.AreEqual("9.9.9.9", obs.LastHopIp);
    }

    [Test]
    public void NullIpHop_WithAnsweredSamples_KeepsItsStats_AndIsNeverTheTargetHop()
    {
        var probe = new NetDiagProbeDto
        {
            Method = "IcmpMtr",
            Outcome = "ok",
            Hops =
            [
                new NetDiagHopDto { Ttl = 1, Ip = "192.168.1.1", Samples = Series(60, answered: 60, rttUs: 1000) },
                new NetDiagHopDto { Ttl = 2, Ip = null, Samples = Series(60, answered: 7, rttUs: 9000) },
            ],
        };
        var built = BuildSingle(probe);
        var hop = built.Measurement.Targets[0].Paths[0].Probes[0].Hops[1];

        Assert.IsNull(hop.Ip);
        Assert.AreEqual((60, 7), (hop.Stats.Sent, hop.Stats.Received));
        // No hop is the target, so the last answering hop is the summary, not the unanswered target fallback.
        var obs = built.Observations.Single();
        Assert.AreEqual((60, 7), (obs.Sent, obs.Received));
        Assert.IsNull(obs.LastHopIp, "the summary hop has no ip");
    }

    [Test]
    public void FinalHop_TargetHopAfterANullIpHop_ComesFromTheTargetHop()
    {
        var probe = new NetDiagProbeDto
        {
            Method = "IcmpMtr",
            Outcome = "ok",
            Hops =
            [
                new NetDiagHopDto { Ttl = 8, Ip = null, Samples = Series(60, answered: 7, rttUs: 9000) },
                new NetDiagHopDto { Ttl = 9, Ip = "9.9.9.9", Samples = Series(60, answered: 60, rttUs: 12000) },
            ],
        };
        var built = BuildSingle(probe);
        var obs = built.Observations.Single();

        Assert.AreEqual((60, 60, 0.0), (obs.Sent, obs.Received, obs.LossPct));
        Assert.AreEqual("9.9.9.9", obs.LastHopIp);
        Assert.AreEqual(12.0, built.Measurement.Targets[0].Paths[0].Probes[0].Stats.P50Ms);
    }

    // The null-ip replies are the target host's Time Exceeded, not answers of the probed service, so they do not
    // make the destination reachable: the summary stays the unanswered target hop.
    [Test]
    public void FinalHop_NullIpRepliesBeforeAnUnansweredTargetHop_DoNotMakeTheTargetReachable()
    {
        var probe = new NetDiagProbeDto
        {
            Method = "IcmpMtr",
            Outcome = "ok",
            Hops =
            [
                new NetDiagHopDto { Ttl = 8, Ip = null, Samples = Series(60, answered: 7, rttUs: 9000) },
                new NetDiagHopDto { Ttl = 9, Ip = "9.9.9.9", Samples = Series(60, answered: 0, rttUs: 0) },
            ],
        };
        var obs = BuildSingle(probe).Observations.Single();

        Assert.AreEqual((60, 0, 100.0), (obs.Sent, obs.Received, obs.LossPct));
        Assert.IsNull(obs.LastHopIp);
    }

    [Test]
    public void FinalHop_TargetIpHopWithoutSamples_IsNotChosen_TheLastAnsweringHopIs()
    {
        var probe = new NetDiagProbeDto
        {
            Method = "IcmpMtr",
            Outcome = "ok",
            Hops =
            [
                new NetDiagHopDto { Ttl = 1, Ip = "10.1.1.1", Samples = Series(60, answered: 60, rttUs: 5000) },
                new NetDiagHopDto { Ttl = 2, Ip = "9.9.9.9", Samples = [] },
            ],
        };
        var obs = BuildSingle(probe).Observations.Single();

        Assert.AreEqual((60, 60), (obs.Sent, obs.Received));
        Assert.AreEqual("10.1.1.1", obs.LastHopIp);
    }

    [Test]
    public void FinalHop_SeveralUnansweredTargetIpHops_TheLowestTtlIsChosen()
    {
        var probe = new NetDiagProbeDto
        {
            Method = "IcmpMtr",
            Outcome = "ok",
            Hops =
            [
                new NetDiagHopDto { Ttl = 6, Ip = "9.9.9.9", Samples = Series(10, answered: 0, rttUs: 0) },
                new NetDiagHopDto { Ttl = 5, Ip = "9.9.9.9", Samples = Series(60, answered: 0, rttUs: 0) },
            ],
        };
        var obs = BuildSingle(probe).Observations.Single();

        Assert.AreEqual((60, 0), (obs.Sent, obs.Received), "ttl 5 has the 60 samples");
    }

    // n samples, the first `answered` of them with the given rtt (µs), the rest lost.
    private static List<long?[]> Series(int n, int answered, long rttUs) =>
        Enumerable.Range(0, n).Select(i => new long?[] { i * 1000L, i < answered ? rttUs : null }).ToList();

    [Test]
    public void Traceroute_HasNoProbeStats_ButTheObservationUsesRoundOneOfTheFinalHop()
    {
        var probe = new NetDiagProbeDto
        {
            Method = "IcmpTraceroute",
            Outcome = "ok",
            Hops = [new NetDiagHopDto { Ttl = 1, Ip = "9.9.9.9", Samples = [new long?[] { 0, 7000 }, new long?[] { 10, 9000 }, new long?[] { 20, 8000 }] }],
        };
        var built = BuildSingle(probe);

        Assert.IsNull(built.Measurement.Targets[0].Paths[0].Probes[0].Stats);
        Assert.AreEqual(8.0, built.Observations.Single().P50Ms);
    }

    [Test]
    public void AllHopsLost_ObservationShowsFullLossOfTheDeepestHop()
    {
        var probe = new NetDiagProbeDto
        {
            Method = "IcmpMtr",
            Outcome = "unreachable",
            Hops = [new NetDiagHopDto { Ttl = 1, Samples = [new long?[] { 0, null }] }, new NetDiagHopDto { Ttl = 2, Samples = [new long?[] { 0, null }, new long?[] { 1, null }] }],
        };
        var obs = BuildSingle(probe).Observations.Single();

        Assert.AreEqual(2, obs.Sent);
        Assert.AreEqual(100.0, obs.LossPct);
        Assert.IsNull(obs.P50Ms);
        Assert.IsNull(obs.LastHopIp);
    }

    [Test]
    public void LookupIps_ClientAndEveryValidHopIp_Distinct() =>
        Assert.AreEqual(new[] { "81.2.69.160", "192.168.1.1", "1.128.0.1", "1.2.3.4" },
            ResultBuilder.LookupIps(SubmissionFixtures.Parse(SubmissionFixtures.FullJson), "81.2.69.160"));

    [TestCase("203.0.113.7", "198.51.100.1", "203.0.113.7")]
    [TestCase(null, "198.51.100.1", "198.51.100.1")]
    [TestCase("not-an-ip", "198.51.100.1", "198.51.100.1")]
    public void ChooseClientIp_PrefersReportedPublicIpv4(string reported, string remote, string expected) =>
        Assert.AreEqual(expected, ResultBuilder.ChooseClientIp(SubmissionFixtures.Minimal("m") with { PublicIpv4 = reported }, remote));

    [Test]
    public void IpInfoRows_OnlyForIpsWithGeo_WithRunScopedRetention()
    {
        var now = SubmittedAt;
        var rows = ResultBuilder.IpInfoRows("run-1", new Dictionary<string, ClientGeo> { ["1.2.3.4"] = Geo["1.2.3.4"], ["10.0.0.1"] = null }, now);

        var row = rows.Single();
        Assert.AreEqual(("run-1", "1.2.3.4", 13335L), (row.RunId, row.Ip, row.Asn.Value));
        Assert.AreEqual(now, row.FirstSeenAt);
        Assert.AreEqual(now.AddDays(365 + 14), row.ExpiresAt);
    }

    // A12 / P9: absent optionals never throw and map to empty stats / null dimensions.
    [TestCase(null)]
    [TestCase("empty")]
    public void HopMethod_WithAbsentOrEmptyHops_GivesZeroSentAndNoLoss(string variant)
    {
        var probe = new NetDiagProbeDto { Method = "IcmpMtr", Outcome = "ok", Hops = variant == null ? null : [] };
        var built = BuildSingle(probe);
        var obs = built.Observations.Single();

        Assert.AreEqual(0, obs.Sent);
        Assert.IsNull(obs.LossPct);
        Assert.AreEqual(0, obs.HopCount);
        Assert.IsNull(obs.LastHopIp);
        Assert.IsNull(built.Measurement.Targets[0].Paths[0].Probes[0].Stats);
        Assert.IsEmpty(built.RawSeries.Series);
    }

    [TestCase(null)]
    [TestCase("empty")]
    public void PingMethod_WithAbsentOrEmptySamples_GivesZeroSent(string variant)
    {
        var probe = new NetDiagProbeDto { Method = "TcpPing", Outcome = "unsupported", Samples = variant == null ? null : [] };
        var built = BuildSingle(probe);
        var obs = built.Observations.Single();

        Assert.AreEqual(0, obs.Sent);
        Assert.IsNull(obs.LossPct);
        Assert.AreEqual(0, obs.HopCount);
        Assert.IsEmpty(built.RawSeries.Series);
    }

    [Test]
    public void PathWithoutEndpoints_GivesNullTargetIp()
    {
        var dto = SubmissionFixtures.Minimal(SubmissionFixtures.MeasurementId) with
        {
            Targets = [new NetDiagTargetDto { TargetKey = "gateway", Paths = [new NetDiagPathDto { Path = "direct", Endpoints = null, Probes = [new NetDiagProbeDto { Method = "TcpPing", Samples = [new long?[] { 0, 1000 }] }] }] }],
        };
        var obs = ResultBuilder.Build(dto, Assignment, SubmittedAt, null, Geo).Observations.Single();

        Assert.IsNull(obs.TargetIp);
        Assert.AreEqual(1, obs.Sent);
    }

    [Test]
    public void SystemPresentButVpnNull_VpnSuspectedIsUnknownOnMeasurementAndObservations()
    {
        var dto = SubmissionFixtures.Parse(SubmissionFixtures.FullJson);
        dto = dto with { System = dto.System with { Vpn = null } };

        var built = ResultBuilder.Build(dto, Assignment, SubmittedAt, null, Geo);

        Assert.IsNull(built.Measurement.VpnSuspected);
        Assert.IsTrue(built.Observations.All(o => o.VpnSuspected == null));
    }

    [Test]
    public void VpnObjectWithoutSuspectedFlag_IsUnknown()
    {
        var dto = SubmissionFixtures.Parse(SubmissionFixtures.FullJson);
        dto = dto with { System = dto.System with { Vpn = new NetDiagVpn { Evidence = ["x"] } } };

        Assert.IsNull(ResultBuilder.Build(dto, Assignment, SubmittedAt, null, Geo).Measurement.VpnSuspected);
    }

    [Test]
    public void VpnReportedFalse_StaysFalse_AndTrueStaysTrue()
    {
        var dto = SubmissionFixtures.Parse(SubmissionFixtures.FullJson);

        foreach (var flag in new[] { true, false })
        {
            var built = ResultBuilder.Build(dto with { System = dto.System with { Vpn = new NetDiagVpn { Suspected = flag } } }, Assignment, SubmittedAt, null, Geo);
            Assert.AreEqual(flag, built.Measurement.VpnSuspected);
            Assert.IsTrue(built.Observations.All(o => o.VpnSuspected == flag));
        }
    }

    [Test]
    public void ProbeWithoutSamples_HasNoStats_ButTheObservationKeepsNumericCountsAndNullStatistics()
    {
        foreach (var method in new[] { "TcpPing", "FloUdpPing" })
        {
            foreach (var samples in new List<long?[]>[] { null, [] })
            {
                var built = BuildSingle(new NetDiagProbeDto { Method = method, Outcome = "unsupported", Samples = samples });
                var obs = built.Observations.Single();

                Assert.IsNull(built.Measurement.Targets[0].Paths[0].Probes[0].Stats, method);
                Assert.AreEqual((0, 0), (obs.Sent, obs.Received));
                Assert.IsNull(obs.LossPct);
                Assert.IsNull(obs.MinMs);
                Assert.IsNull(obs.AvgMs);
                Assert.IsNull(obs.MaxMs);
                Assert.IsNull(obs.StddevMs);
                Assert.IsNull(obs.P50Ms);
                Assert.IsNull(obs.P90Ms);
                Assert.IsNull(obs.P95Ms);
                Assert.IsNull(obs.P99Ms);
                Assert.IsNull(obs.JitterMs);
            }
        }
    }

    [Test]
    public void ProbeThatSentSamplesAndLostAll_KeepsStats_WithFullLoss()
    {
        var built = BuildSingle(new NetDiagProbeDto { Method = "TcpPing", Outcome = "unreachable", Samples = [new long?[] { 0, null }, new long?[] { 1, null }] });

        var stats = built.Measurement.Targets[0].Paths[0].Probes[0].Stats;
        Assert.AreEqual((2, 0, 100.0), (stats.Sent, stats.Received, stats.LossPct));
        Assert.AreEqual(100.0, built.Observations.Single().LossPct);
    }

    [Test]
    public void MtrWhereEveryHopHasNoSamples_HasNoProbeStats_AndZeroCountObservation()
    {
        var built = BuildSingle(new NetDiagProbeDto
        {
            Method = "IcmpMtr",
            Outcome = "unreachable",
            Hops = [new NetDiagHopDto { Ttl = 1, Ip = "10.0.0.1" }, new NetDiagHopDto { Ttl = 2, Ip = "10.0.0.2", Samples = [] }],
        });

        var probe = built.Measurement.Targets[0].Paths[0].Probes[0];
        var obs = built.Observations.Single();
        Assert.IsNull(probe.Stats);
        Assert.IsTrue(probe.Hops.All(h => h.Stats == null));
        Assert.AreEqual((0, 0, 2), (obs.Sent, obs.Received, obs.HopCount));
        Assert.IsNull(obs.LossPct);
        Assert.IsNull(obs.P50Ms);
    }

    [Test]
    public void HopWithoutSamples_HasNoStats_WhileHopsWithSamplesKeepTheirs()
    {
        var built = BuildSingle(new NetDiagProbeDto
        {
            Method = "IcmpMtr",
            Outcome = "ok",
            Hops = [new NetDiagHopDto { Ttl = 1, Ip = "10.0.0.1" }, new NetDiagHopDto { Ttl = 2, Ip = "10.0.0.2", Samples = [] }, new NetDiagHopDto { Ttl = 3, Ip = "10.0.0.3", Samples = [new long?[] { 0, 5000 }] }],
        });

        var probe = built.Measurement.Targets[0].Paths[0].Probes[0];
        Assert.IsNull(probe.Hops[0].Stats);
        Assert.IsNull(probe.Hops[1].Stats);
        Assert.AreEqual(5.0, probe.Hops[2].Stats.P50Ms);
        Assert.AreEqual(5.0, probe.Stats.P50Ms);
    }

    [Test]
    public void NullSystemAndOs_BuildWithoutThrowing()
    {
        var dto = SubmissionFixtures.Minimal(SubmissionFixtures.MeasurementId) with { Os = null, System = null, FloSnapshot = null, Targets = null };
        var m = ResultBuilder.Build(dto, Assignment, SubmittedAt, null, Geo).Measurement;

        Assert.IsNull(m.Os);
        Assert.IsNull(m.OsInfo);
        Assert.IsNull(m.System);
        Assert.IsNull(m.PrimaryAdapterType);
        Assert.IsNull(m.VpnSuspected, "no system object: VPN state is unknown, not false");
        Assert.IsNull(m.ClientGeo);
        Assert.AreEqual(0, m.TargetCount);
        Assert.IsEmpty(m.FloSnapshot);
    }

    // P11
    [Test]
    public void StringLists_AreTruncatedTo64_InTheStoredResult()
    {
        List<string> many(string prefix) => Enumerable.Range(0, 100).Select(i => $"{prefix}{i}").ToList();
        var dto = SubmissionFixtures.Minimal(SubmissionFixtures.MeasurementId) with
        {
            System = new NetDiagSystem
            {
                Adapters = [new NetDiagAdapter { Kind = "ethernet", HasDefaultRoute = true, Dns = many("d") }],
                Vpn = new NetDiagVpn { Suspected = true, Evidence = many("e") },
                Antivirus = new NetDiagAntivirus { ThirdParty = many("a") },
            },
        };
        var system = ResultBuilder.Build(dto, Assignment, SubmittedAt, null, Geo).Measurement.System;

        Assert.AreEqual(64, system.Adapters.Single().Dns.Count);
        Assert.AreEqual("d63", system.Adapters.Single().Dns[^1]);
        Assert.AreEqual(64, system.Vpn.Evidence.Count);
        Assert.AreEqual(64, system.Antivirus.ThirdParty.Count);
        Assert.AreEqual(100, dto.System.Vpn.Evidence.Count, "the input is not mutated");
    }

    [Test]
    public void NullElementsOfFloSnapshotAndAdapters_AreDropped()
    {
        var dto = SubmissionFixtures.Minimal(SubmissionFixtures.MeasurementId) with
        {
            FloSnapshot = [null, new NetDiagFloSnapshotRow { NodeId = 3 }, null],
            System = new NetDiagSystem { Adapters = [null, new NetDiagAdapter { Kind = "wifi", HasDefaultRoute = true }] },
        };
        var m = ResultBuilder.Build(dto, Assignment, SubmittedAt, null, Geo).Measurement;

        Assert.AreEqual(3, m.FloSnapshot.Single().NodeId);
        Assert.AreEqual("wifi", m.System.Adapters.Single().Kind);
        Assert.AreEqual("wifi", m.PrimaryAdapterType);
    }

    [Test]
    public void LauncherProducedJson_BuildsWithoutThrowing()
    {
        var built = ResultBuilder.Build(SubmissionFixtures.Parse(SubmissionFixtures.LauncherProducedJson), Assignment, SubmittedAt, "203.0.113.7", Geo);

        Assert.AreEqual(4294967295L, built.Measurement.Restarts);
        Assert.AreEqual(4, built.Observations.Count);
    }

    private static BuiltResult BuildSingle(NetDiagProbeDto probe)
    {
        var dto = SubmissionFixtures.Minimal(SubmissionFixtures.MeasurementId) with
        {
            Targets = [new NetDiagTargetDto { TargetKey = "baseline:quad9", Label = "Quad9", Paths = [new NetDiagPathDto { Path = "direct", Endpoints = new NetDiagEndpoints { Icmp = "9.9.9.9", Tcp = "9.9.9.9:443" }, Probes = [probe] }] }],
        };
        return ResultBuilder.Build(dto, Assignment, SubmittedAt, "81.2.69.160", Geo);
    }
}
