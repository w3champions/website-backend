using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using W3ChampionsStatisticService.NetworkDiagnostics;
using W3ChampionsStatisticService.NetworkDiagnostics.Submission;

namespace WC3ChampionsStatisticService.Tests.NetworkDiagnostics;

[TestFixture]
public class SubmissionValidatorTests
{
    private const string Id = SubmissionFixtures.MeasurementId;

    private static NetDiagSubmission Full() => SubmissionFixtures.Parse(SubmissionFixtures.FullJson);

    private static NetDiagSubmission WithProbe(NetDiagProbeDto probe) => SubmissionFixtures.Minimal(Id) with
    {
        Targets = [new NetDiagTargetDto { TargetKey = "gateway", Paths = [new NetDiagPathDto { Path = "direct", Probes = [probe] }] }],
    };

    private static List<long?[]> Samples(int n) => Enumerable.Range(0, n).Select(i => new long?[] { i * 1000L, 1000 }).ToList();

    [Test]
    public void FullFixture_IsValid() => Assert.IsNull(SubmissionValidator.ValidateSubmission(Full(), Id));

    [Test]
    public void NullBody() => Assert.AreEqual("Missing body", SubmissionValidator.ValidateSubmission(null, Id));

    [Test]
    public void MeasurementIdMismatch() =>
        Assert.AreEqual("measurement_id does not match the URL", SubmissionValidator.ValidateSubmission(Full(), "other"));

    [Test]
    public void NullTargets_AreTreatedAsEmpty() =>
        Assert.IsNull(SubmissionValidator.ValidateSubmission(SubmissionFixtures.Minimal(Id) with { Targets = null }, Id));

    [Test]
    public void TooManyTargets()
    {
        var targets = Enumerable.Range(0, 201).Select(i => new NetDiagTargetDto { TargetKey = $"custom:10.0.0.{i % 250}:443" }).ToList();
        Assert.AreEqual("Too many targets", SubmissionValidator.ValidateSubmission(SubmissionFixtures.Minimal(Id) with { Targets = targets }, Id));
    }

    [Test]
    public void TooManyPaths()
    {
        var target = new NetDiagTargetDto { TargetKey = "gateway", Paths = [new(), new(), new()] };
        Assert.AreEqual("Too many paths", SubmissionValidator.ValidateSubmission(SubmissionFixtures.Minimal(Id) with { Targets = [target] }, Id));
    }

    [Test]
    public void TooManyProbes()
    {
        var path = new NetDiagPathDto { Path = "direct", Probes = Enumerable.Range(0, 7).Select(_ => new NetDiagProbeDto { Method = "TcpPing" }).ToList() };
        var target = new NetDiagTargetDto { TargetKey = "gateway", Paths = [path] };
        Assert.AreEqual("Too many probes", SubmissionValidator.ValidateSubmission(SubmissionFixtures.Minimal(Id) with { Targets = [target] }, Id));
    }

    [Test]
    public void ThirtyOneHops_AreAllowed_ThirtyRoutersPlusTheDestinationHop()
    {
        var hops = Enumerable.Range(1, 31).Select(t => new NetDiagHopDto { Ttl = t }).ToList();
        Assert.IsNull(SubmissionValidator.ValidateSubmission(WithProbe(new NetDiagProbeDto { Method = "IcmpMtr", Hops = hops }), Id));
    }

    [TestCase(null)]
    [TestCase(0L)]
    [TestCase(2000L)]
    [TestCase(60_000_000L)]
    public void IcmpSendGapUs_InRangeOrNull_IsValid(long? gap) =>
        Assert.IsNull(SubmissionValidator.ValidateSubmission(SubmissionFixtures.Minimal(Id) with { Pacing = new NetDiagPacing { IcmpSendGapUs = gap } }, Id));

    [TestCase(-1L)]
    [TestCase(60_000_001L)]
    [TestCase(long.MaxValue)]
    public void IcmpSendGapUs_NegativeOrAbsurd_IsRejected(long gap) =>
        Assert.AreEqual("icmp_send_gap_us out of range",
            SubmissionValidator.ValidateSubmission(SubmissionFixtures.Minimal(Id) with { Pacing = new NetDiagPacing { IcmpSendGapUs = gap } }, Id));

    [Test]
    public void TooManyHops()
    {
        var hops = Enumerable.Range(1, 32).Select(t => new NetDiagHopDto { Ttl = t }).ToList();
        Assert.AreEqual("Too many hops", SubmissionValidator.ValidateSubmission(WithProbe(new NetDiagProbeDto { Method = "IcmpMtr", Hops = hops }), Id));
    }

    [Test]
    public void SampleCap_600Ok_601Rejected()
    {
        Assert.IsNull(SubmissionValidator.ValidateSubmission(WithProbe(new NetDiagProbeDto { Method = "TcpPing", Samples = Samples(600) }), Id));
        Assert.AreEqual("Too many samples", SubmissionValidator.ValidateSubmission(WithProbe(new NetDiagProbeDto { Method = "TcpPing", Samples = Samples(601) }), Id));
        var hop = new NetDiagHopDto { Ttl = 1, Samples = Samples(601) };
        Assert.AreEqual("Too many samples", SubmissionValidator.ValidateSubmission(WithProbe(new NetDiagProbeDto { Method = "IcmpMtr", Hops = [hop] }), Id));
    }

    [TestCase(new long[] { 0 }, "Malformed sample")]
    [TestCase(new long[] { -1, 5 }, "Sample offset out of range")]
    [TestCase(new long[] { 4294967296, 5 }, "Sample offset out of range")]
    [TestCase(new long[] { 0, 4294967295 }, "Sample rtt out of range")]
    [TestCase(new long[] { 0, -2 }, "Sample rtt out of range")]
    public void MalformedSamples(long[] pair, string expected)
    {
        var samples = new List<long?[]> { pair.Select(v => (long?)v).ToArray() };
        Assert.AreEqual(expected, SubmissionValidator.ValidateSubmission(WithProbe(new NetDiagProbeDto { Method = "TcpPing", Samples = samples }), Id));
    }

    [Test]
    public void NullOffset_IsMalformed()
    {
        var samples = new List<long?[]> { new long?[] { null, 5 } };
        Assert.AreEqual("Malformed sample", SubmissionValidator.ValidateSubmission(WithProbe(new NetDiagProbeDto { Method = "TcpPing", Samples = samples }), Id));
    }

    // P11: the launcher's hop ttl is a u8, so 0..255 are all valid on the wire.
    [TestCase(-1)]
    [TestCase(256)]
    public void HopTtlOutOfRange(int ttl) =>
        Assert.AreEqual("hop ttl out of range",
            SubmissionValidator.ValidateSubmission(WithProbe(new NetDiagProbeDto { Method = "IcmpMtr", Hops = [new NetDiagHopDto { Ttl = ttl }] }), Id));

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(255)]
    public void HopTtl_0To255_IsAccepted(int ttl) =>
        Assert.IsNull(SubmissionValidator.ValidateSubmission(WithProbe(new NetDiagProbeDto { Method = "IcmpMtr", Hops = [new NetDiagHopDto { Ttl = ttl }] }), Id));

    [Test]
    public void TooManyAdaptersAndSnapshotRows()
    {
        var adapters = Enumerable.Range(0, 65).Select(_ => new NetDiagAdapter()).ToList();
        Assert.AreEqual("Too many adapters", SubmissionValidator.ValidateSubmission(SubmissionFixtures.Minimal(Id) with { System = new NetDiagSystem { Adapters = adapters } }, Id));
        var rows = Enumerable.Range(0, 301).Select(i => new NetDiagFloSnapshotRow { NodeId = i }).ToList();
        Assert.AreEqual("Too many flo_snapshot rows", SubmissionValidator.ValidateSubmission(SubmissionFixtures.Minimal(Id) with { FloSnapshot = rows }, Id));
    }

    // P11 (supersedes the "<= 64 entries" reject rule): long lists are truncated later by the result builder.
    [Test]
    public void StringListsLongerThan64_AreNotRejected()
    {
        var many = Enumerable.Range(0, 100).Select(i => $"e{i}").ToList();
        var system = new NetDiagSystem
        {
            Vpn = new NetDiagVpn { Evidence = many },
            Antivirus = new NetDiagAntivirus { ThirdParty = many },
            Adapters = [new NetDiagAdapter { Dns = many }],
        };
        Assert.IsNull(SubmissionValidator.ValidateSubmission(SubmissionFixtures.Minimal(Id) with { System = system }, Id));
    }

    [Test]
    public void EachEntryOfALongStringList_IsStillCappedAt500()
    {
        var many = Enumerable.Range(0, 100).Select(i => i == 99 ? new string('x', 501) : $"e{i}").ToList();
        var system = new NetDiagSystem { Vpn = new NetDiagVpn { Evidence = many } };
        Assert.AreEqual("String too long", SubmissionValidator.ValidateSubmission(SubmissionFixtures.Minimal(Id) with { System = system }, Id));
    }

    // P11: Task 5 drops null elements of flo_snapshot/adapters; the validator must not throw on them.
    [Test]
    public void NullElementsInSnapshotAdaptersAndStringLists_DoNotThrow()
    {
        var system = new NetDiagSystem
        {
            Adapters = [null, new NetDiagAdapter { Dns = [null, "1.1.1.1"] }],
            Vpn = new NetDiagVpn { Evidence = [null] },
            Antivirus = new NetDiagAntivirus { ThirdParty = [null] },
        };
        var dto = SubmissionFixtures.Minimal(Id) with { System = system, FloSnapshot = [null, new NetDiagFloSnapshotRow { NodeId = 1 }] };
        Assert.IsNull(SubmissionValidator.ValidateSubmission(dto, Id));
    }

    [Test]
    public void NullElementsInSnapshotAndAdapters_ParsedFromJson_DoNotThrow()
    {
        const string json = """
        { "measurement_id": "m", "system": { "adapters": [null, { "name": "a", "dns": [null] }] }, "flo_snapshot": [null] }
        """;
        Assert.IsNull(SubmissionValidator.ValidateSubmission(SubmissionFixtures.Parse(json), "m"));
    }

    // A8: reasons are open-ended strings; only the 500-char cap applies.
    [TestCase("os_requires_admin")]
    [TestCase("some_reason_this_server_has_never_seen")]
    public void UnknownReason_IsAcceptedVerbatim(string reason)
    {
        Assert.IsNull(SubmissionValidator.ValidateSubmission(WithProbe(new NetDiagProbeDto { Method = "TcpMtr", Outcome = "unsupported", Reason = reason }), Id));
        Assert.IsNull(SubmissionValidator.ValidateSubmission(WithProbe(new NetDiagProbeDto { Method = "TcpMtr", Outcome = "unsupported", Reason = new string('r', 500) }), Id));
    }

    [Test]
    public void AnyStringOver500_IsRejected_IncludingNestedOnes()
    {
        var longText = new string('x', 501);
        Assert.AreEqual("String too long", SubmissionValidator.ValidateSubmission(SubmissionFixtures.Minimal(Id) with { LauncherVersion = longText }, Id));
        Assert.AreEqual("String too long", SubmissionValidator.ValidateSubmission(WithProbe(new NetDiagProbeDto { Method = "TcpPing", Reason = longText }), Id));
        var adapter = new NetDiagAdapter { Dns = [longText] };
        Assert.AreEqual("String too long", SubmissionValidator.ValidateSubmission(SubmissionFixtures.Minimal(Id) with { System = new NetDiagSystem { Adapters = [adapter] } }, Id));
        Assert.IsNull(SubmissionValidator.ValidateSubmission(SubmissionFixtures.Minimal(Id) with { LauncherVersion = new string('x', 500) }, Id));
    }

    [Test]
    public void NullSystemSubObjects_AreAllowed() =>
        Assert.IsNull(SubmissionValidator.ValidateSubmission(SubmissionFixtures.Minimal(Id) with { System = new NetDiagSystem(), Pacing = null, Os = null }, Id));

    [Test]
    public void ToSamples_MapsNullToLost() =>
        Assert.AreEqual(
            new[] { new ProbeSample(0, 5), new ProbeSample(1000, null) },
            SubmissionValidator.ToSamples([new long?[] { 0, 5 }, new long?[] { 1000, null }]));
}
