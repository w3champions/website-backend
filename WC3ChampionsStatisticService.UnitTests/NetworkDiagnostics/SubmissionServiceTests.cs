using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using W3ChampionsStatisticService.NetworkDiagnostics;
using W3ChampionsStatisticService.NetworkDiagnostics.Api;
using W3ChampionsStatisticService.NetworkDiagnostics.GeoIp;
using W3ChampionsStatisticService.NetworkDiagnostics.Persistence;
using W3ChampionsStatisticService.NetworkDiagnostics.Results;
using W3ChampionsStatisticService.NetworkDiagnostics.Runs;
using W3ChampionsStatisticService.NetworkDiagnostics.Submission;

namespace WC3ChampionsStatisticService.Tests.NetworkDiagnostics;

[TestFixture]
public class SubmissionServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);
    private const string Id = SubmissionFixtures.MeasurementId;
    private const string Player = "Name#1234";
    private const string SocketIp = "198.51.100.1";

    private Mock<INetworkDiagnosticsAssignmentRepository> _assignments;
    private Mock<INetworkDiagnosticsRunRepository> _runs;
    private Mock<INetworkDiagnosticsResultRepository> _results;
    private Mock<IGeoIpLookup> _geo;
    private NetworkDiagnosticsSubmissionService _service;
    private NetworkDiagnosticAssignment _assignment;
    private ListLogger<NetworkDiagnosticsSubmissionService> _log;
    private string _token;
    private BuiltResult _built;

    [SetUp]
    public void SetUp()
    {
        _token = MeasurementTokens.NewToken();
        _assignment = new NetworkDiagnosticAssignment
        {
            Id = Id,
            RunId = "run-1",
            BattleTag = Player,
            Token = _token,
            Status = AssignmentStatus.Pending,
            DeliverAfter = Now.AddMinutes(-5),
            DeliverBy = Now.AddDays(14),
        };
        _log = new ListLogger<NetworkDiagnosticsSubmissionService>();
        _assignments = new Mock<INetworkDiagnosticsAssignmentRepository>();
        _assignments.Setup(a => a.GetAsync(Id)).ReturnsAsync(_assignment);
        _runs = new Mock<INetworkDiagnosticsRunRepository>();
        _results = new Mock<INetworkDiagnosticsResultRepository>();
        _built = null;
        _bodyReads = 0;
        _results.Setup(r => r.InsertResultAsync(It.IsAny<BuiltResult>())).Callback<BuiltResult>(b => _built = b).Returns(Task.CompletedTask);
        _geo = new Mock<IGeoIpLookup>();
        _geo.Setup(g => g.Lookup(It.IsAny<IEnumerable<string>>())).Returns(new Dictionary<string, ClientGeo>
        {
            ["81.2.69.160"] = new() { Country = "GB", Asn = 20712 },
            ["1.2.3.4"] = new() { Country = "US", Asn = 13335 },
        });
        _service = new NetworkDiagnosticsSubmissionService(_assignments.Object, _runs.Object, _results.Object, _geo.Object,
            new FixedTimeProvider(new DateTimeOffset(Now)), _log);
    }

    private int _bodyReads;

    /// <summary>The controller's body reader: counts how often the service asked for the body.</summary>
    private Func<Task<(NetDiagSubmission Dto, string Error)>> Body(NetDiagSubmission dto, string error = null) => () =>
    {
        _bodyReads++;
        return Task.FromResult((dto, error));
    };

    private static NetDiagSubmission Full() => SubmissionFixtures.Parse(SubmissionFixtures.FullJson);

    private static string BodyOf(NetDiagResult r) => JsonSerializer.Serialize(r.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private void TheConditionalUpdateSucceeds() =>
        _assignments.Setup(a => a.TrySubmitAsync(Id, Player, Now))
            .ReturnsAsync(_assignment with { Status = AssignmentStatus.Submitted, SubmittedAt = Now });

    private void VerifyNoConditionalUpdate() =>
        _assignments.Verify(a => a.TrySubmitAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);

    private void VerifyNothingWritten()
    {
        _results.Verify(r => r.InsertResultAsync(It.IsAny<BuiltResult>()), Times.Never);
        _results.Verify(r => r.UpsertIpInfoAsync(It.IsAny<IReadOnlyCollection<NetworkDiagnosticIpInfo>>()), Times.Never);
        _runs.Verify(r => r.IncrementCountersAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task Valid_Returns201_AndWritesEachDocumentOnce()
    {
        var calls = new List<string>();
        _assignments.Setup(a => a.TrySubmitAsync(Id, Player, Now)).Callback(() => calls.Add("update"))
            .ReturnsAsync(_assignment with { Status = AssignmentStatus.Submitted, SubmittedAt = Now });
        _results.Setup(r => r.InsertResultAsync(It.IsAny<BuiltResult>())).Callback<BuiltResult>(b => { _built = b; calls.Add("insert"); }).Returns(Task.CompletedTask);
        _results.Setup(r => r.UpsertIpInfoAsync(It.IsAny<IReadOnlyCollection<NetworkDiagnosticIpInfo>>())).Callback(() => calls.Add("ipInfo")).Returns(Task.CompletedTask);
        _geo.Setup(g => g.Lookup(It.IsAny<IEnumerable<string>>())).Callback(() => calls.Add("geo")).Returns(new Dictionary<string, ClientGeo>
        {
            ["81.2.69.160"] = new() { Country = "GB", Asn = 20712 },
            ["1.2.3.4"] = new() { Country = "US", Asn = 13335 },
        });

        var result = await _service.SubmitAsync(Id, Player, _token, Body(Full()), SocketIp);

        Assert.AreEqual(201, result.StatusCode);
        Assert.AreEqual($$"""{"measurementId":"{{Id}}"}""", BodyOf(result));
        // Only the insert sits between the conditional update and the answer that matters; enrichment comes after it.
        CollectionAssert.AreEqual(new[] { "geo", "update", "insert", "ipInfo" }, calls);
        Assert.AreEqual(1, _bodyReads);
        Assert.AreEqual("run-1", _built.Measurement.RunId);
        Assert.AreEqual(Now, _built.Measurement.SubmittedAt);
        Assert.AreEqual("81.2.69.160", _built.Measurement.ClientIp, "reported public_ipv4 wins over the socket address");
        Assert.AreEqual("GB", _built.Measurement.ClientGeo.Country);
        Assert.AreEqual(13335, _built.Observations[0].LastHopAsn);
        _results.Verify(r => r.UpsertIpInfoAsync(It.Is<IReadOnlyCollection<NetworkDiagnosticIpInfo>>(rows => rows.Count == 2)), Times.Once);
        _results.Verify(r => r.InsertResultAsync(It.IsAny<BuiltResult>()), Times.Once);
        _runs.Verify(r => r.IncrementCountersAsync("run-1", 0, 0, 1, 0), Times.Once);
    }

    [Test]
    public async Task InvalidPayload_400_BeforeAnyUpdate()
    {
        var result = await _service.SubmitAsync(Id, Player, _token, Body(Full() with { MeasurementId = "other-id" }), SocketIp);

        Assert.AreEqual(400, result.StatusCode);
        Assert.AreEqual("""{"error":"measurement_id does not match the URL"}""", BodyOf(result));
        VerifyNoConditionalUpdate();
        VerifyNothingWritten();
    }

    // What the body reader could not read or parse is the 400 it names.
    [TestCase("payload_too_large")]
    [TestCase("Malformed JSON")]
    [TestCase("Missing body")]
    public async Task UnreadableBody_400_BeforeAnyUpdate(string readerError)
    {
        var result = await _service.SubmitAsync(Id, Player, _token, Body(null, readerError), SocketIp);

        Assert.AreEqual(400, result.StatusCode);
        Assert.AreEqual($$"""{"error":"{{readerError}}"}""", BodyOf(result));
        _geo.Verify(g => g.Lookup(It.IsAny<IEnumerable<string>>()), Times.Never);
        VerifyNoConditionalUpdate();
        VerifyNothingWritten();
    }

    // The route id, the body's measurement_id and the assignment id are compared as they are: no case folding.
    [Test]
    public async Task RouteIdInAnotherCase_IsNotTheSameMeasurement()
    {
        _assignments.Setup(a => a.GetAsync(It.IsAny<string>())).ReturnsAsync(_assignment);

        var result = await _service.SubmitAsync(Id.ToUpperInvariant(), Player, _token, Body(Full()), SocketIp);

        Assert.AreEqual(400, result.StatusCode);
        VerifyNoConditionalUpdate();
        VerifyNothingWritten();
    }

    // A body over a C5 cap is rejected before the result builder or the GeoIP lookup see it.
    [Test]
    public async Task OverACap_400_BeforeGeoLookupAndBuild()
    {
        var dto = Full() with { Targets = Enumerable.Repeat(new NetDiagTargetDto { TargetKey = "gateway" }, 201).ToList() };

        var result = await _service.SubmitAsync(Id, Player, _token, Body(dto), SocketIp);

        Assert.AreEqual("""{"error":"Too many targets"}""", BodyOf(result));
        _geo.Verify(g => g.Lookup(It.IsAny<IEnumerable<string>>()), Times.Never);
        VerifyNoConditionalUpdate();
        VerifyNothingWritten();
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]   // right length, wrong value
    [TestCase("AAAA")]                                          // wrong length
    public async Task WrongOrMissingToken_403_BeforeAnyUpdate(string token)
    {
        var result = await _service.SubmitAsync(Id, Player, token, Body(Full()), SocketIp);

        Assert.AreEqual(403, result.StatusCode);
        Assert.AreEqual("""{"error":"forbidden"}""", BodyOf(result));
        Assert.AreEqual(0, _bodyReads, "the body is not read for a caller without the token");
        if (string.IsNullOrEmpty(token)) _assignments.Verify(a => a.GetAsync(It.IsAny<string>()), Times.Never);
        VerifyNoConditionalUpdate();
        VerifyNothingWritten();
    }

    [Test]
    public async Task TokenOneCharacterOff_403()
    {
        var wrong = _token[..^1] + (_token[^1] == 'A' ? 'B' : 'A');

        Assert.AreEqual(403, (await _service.SubmitAsync(Id, Player, wrong, Body(Full()), SocketIp)).StatusCode);
        Assert.AreEqual(0, _bodyReads);
        VerifyNoConditionalUpdate();
        VerifyNothingWritten();
    }

    // A wrong token must not reveal whether the slot was already used or has expired.
    [TestCase(AssignmentStatus.Submitted)]
    [TestCase(AssignmentStatus.Expired)]
    public async Task WrongToken_OnAFinishedAssignment_Is403_Not409(string status)
    {
        _assignments.Setup(a => a.GetAsync(Id)).ReturnsAsync(_assignment with { Status = status });

        Assert.AreEqual(403, (await _service.SubmitAsync(Id, Player, "wrong", Body(Full()), SocketIp)).StatusCode);
        Assert.AreEqual(0, _bodyReads);
    }

    [Test]
    public async Task AlreadySubmitted_Returns409_AndWritesNothing()
    {
        _assignments.Setup(a => a.GetAsync(Id)).ReturnsAsync(_assignment with { Status = AssignmentStatus.Submitted, SubmittedAt = Now.AddMinutes(-1) });

        var result = await _service.SubmitAsync(Id, Player, _token, Body(Full()), SocketIp);

        Assert.AreEqual(409, result.StatusCode);
        Assert.AreEqual("""{"error":"already_submitted"}""", BodyOf(result));
        VerifyNoConditionalUpdate();
        VerifyNothingWritten();
    }

    // Two concurrent submits: both pass the read, only one wins the conditional update; the loser gets 409.
    [Test]
    public async Task LostTheRace_Returns409_AndWritesNothing()
    {
        _assignments.SetupSequence(a => a.GetAsync(Id))
            .ReturnsAsync(_assignment)
            .ReturnsAsync(_assignment with { Status = AssignmentStatus.Submitted, SubmittedAt = Now });
        _assignments.Setup(a => a.TrySubmitAsync(Id, Player, Now)).ReturnsAsync((NetworkDiagnosticAssignment)null);

        var result = await _service.SubmitAsync(Id, Player, _token, Body(Full()), SocketIp);

        Assert.AreEqual(409, result.StatusCode);
        Assert.AreEqual("""{"error":"already_submitted"}""", BodyOf(result));
        VerifyNothingWritten();
    }

    // The run was cancelled between the read and the conditional update.
    [Test]
    public async Task ExpiredDuringTheSubmit_Returns409Expired_AndWritesNothing()
    {
        _assignments.SetupSequence(a => a.GetAsync(Id))
            .ReturnsAsync(_assignment)
            .ReturnsAsync(_assignment with { Status = AssignmentStatus.Expired });
        _assignments.Setup(a => a.TrySubmitAsync(Id, Player, Now)).ReturnsAsync((NetworkDiagnosticAssignment)null);

        var result = await _service.SubmitAsync(Id, Player, _token, Body(Full()), SocketIp);

        Assert.AreEqual(409, result.StatusCode);
        Assert.AreEqual("""{"error":"expired"}""", BodyOf(result));
        VerifyNothingWritten();
    }

    // A10: the token is never logged, on success or on failure.
    [Test]
    public async Task Token_IsNeverLogged()
    {
        _assignments.Setup(a => a.TrySubmitAsync(Id, Player, Now)).ReturnsAsync(_assignment);
        await _service.SubmitAsync(Id, Player, _token, Body(Full()), SocketIp);
        _results.Setup(r => r.InsertResultAsync(It.IsAny<BuiltResult>())).ThrowsAsync(new TimeoutException("mongo"));
        await _service.SubmitAsync(Id, Player, _token, Body(Full()), SocketIp);
        await _service.SubmitAsync(Id, Player, "wrong", Body(Full()), SocketIp);

        Assert.AreEqual(1, _log.Count(LogLevel.Information));
        Assert.AreEqual(1, _log.Count(LogLevel.Error));
        StringAssert.DoesNotContain(_token, _log.AllText);
        StringAssert.DoesNotContain("wrong", _log.AllText);
    }

    [TestCase(AssignmentStatus.Expired, 14)]
    [TestCase(AssignmentStatus.Pending, -1)]   // past deliverBy, not yet swept by the dispatcher
    [TestCase(AssignmentStatus.Pending, 0)]    // deliverBy is exclusive
    public async Task Expired_Returns409(string status, int deliverByDays)
    {
        _assignments.Setup(a => a.GetAsync(Id)).ReturnsAsync(_assignment with { Status = status, DeliverBy = Now.AddDays(deliverByDays) });

        var result = await _service.SubmitAsync(Id, Player, _token, Body(Full()), SocketIp);

        Assert.AreEqual(409, result.StatusCode);
        Assert.AreEqual("""{"error":"expired"}""", BodyOf(result));
        VerifyNoConditionalUpdate();
        VerifyNothingWritten();
    }

    [Test]
    public async Task SomeoneElsesAssignment_Or_Unknown_403()
    {
        _assignments.Setup(a => a.GetAsync(Id)).ReturnsAsync(_assignment with { BattleTag = "Other#1111" });
        Assert.AreEqual(403, (await _service.SubmitAsync(Id, Player, _token, Body(Full()), SocketIp)).StatusCode);

        // BattleTags are matched exactly, no case folding.
        _assignments.Setup(a => a.GetAsync(Id)).ReturnsAsync(_assignment with { BattleTag = Player.ToUpperInvariant() });
        Assert.AreEqual(403, (await _service.SubmitAsync(Id, Player, _token, Body(Full()), SocketIp)).StatusCode);

        _assignments.Setup(a => a.GetAsync(Id)).ReturnsAsync((NetworkDiagnosticAssignment)null);
        Assert.AreEqual(403, (await _service.SubmitAsync(Id, Player, _token, Body(Full()), SocketIp)).StatusCode);
        Assert.AreEqual(0, _bodyReads, "the body is not read for someone else's or an unknown measurement");
        VerifyNoConditionalUpdate();
        VerifyNothingWritten();
    }

    [Test]
    public async Task WriteFailureAfterTheUpdate_500_LoggedWithMeasurementRunAndBattleTag()
    {
        _assignments.Setup(a => a.TrySubmitAsync(Id, Player, Now)).ReturnsAsync(_assignment);
        _results.Setup(r => r.InsertResultAsync(It.IsAny<BuiltResult>())).ThrowsAsync(new TimeoutException("mongo"));

        var result = await _service.SubmitAsync(Id, Player, _token, Body(Full()), SocketIp);

        Assert.AreEqual(500, result.StatusCode);
        Assert.AreEqual("""{"error":"internal_error"}""", BodyOf(result));
        Assert.AreEqual(1, _log.Count(LogLevel.Error));
        StringAssert.Contains(Id, _log.AllText);
        StringAssert.Contains("run-1", _log.AllText);
        StringAssert.Contains(Player, _log.AllText);
        _results.Verify(r => r.UpsertIpInfoAsync(It.IsAny<IReadOnlyCollection<NetworkDiagnosticIpInfo>>()), Times.Never);
        _runs.Verify(r => r.IncrementCountersAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    // The result is stored; only the run's counter is behind. Answering 500 would make the launcher retry into a 409.
    [Test]
    public async Task CounterFailureAfterTheInsert_Still201_AndLogged()
    {
        TheConditionalUpdateSucceeds();
        _runs.Setup(r => r.IncrementCountersAsync("run-1", 0, 0, 1, 0)).ThrowsAsync(new TimeoutException("mongo"));

        var result = await _service.SubmitAsync(Id, Player, _token, Body(Full()), SocketIp);

        Assert.AreEqual(201, result.StatusCode);
        Assert.IsNotNull(_built);
        Assert.AreEqual(1, _log.Count(LogLevel.Error));
        StringAssert.Contains("run-1", _log.AllText);
    }

    // Review Focus 4: GeoIP is enrichment. A lookup that has nothing (files missing) or that throws costs neither the
    // result nor a retry: it is stored without geo.
    [TestCase(false)]
    [TestCase(true)]
    public async Task GeoIpUnavailable_StillStores_WithNullGeo(bool lookupThrows)
    {
        TheConditionalUpdateSucceeds();
        if (lookupThrows) _geo.Setup(g => g.Lookup(It.IsAny<IEnumerable<string>>())).Throws(new InvalidOperationException("geo"));
        else _geo.Setup(g => g.Lookup(It.IsAny<IEnumerable<string>>())).Returns((IReadOnlyDictionary<string, ClientGeo>)null);

        var result = await _service.SubmitAsync(Id, Player, _token, Body(Full()), SocketIp);

        Assert.AreEqual(201, result.StatusCode);
        Assert.AreEqual("81.2.69.160", _built.Measurement.ClientIp);
        Assert.IsNull(_built.Measurement.ClientGeo);
        Assert.AreEqual(4, _built.Observations.Count);
        _results.Verify(r => r.UpsertIpInfoAsync(It.Is<IReadOnlyCollection<NetworkDiagnosticIpInfo>>(rows => rows.Count == 0)), Times.Once);
        Assert.AreEqual(0, _log.Count(LogLevel.Error));
        Assert.AreEqual(lookupThrows ? 1 : 0, _log.Count(LogLevel.Warning));
        if (lookupThrows)
        {
            StringAssert.Contains(Id, _log.AllText);
            StringAssert.Contains("run-1", _log.AllText);
            StringAssert.DoesNotContain("81.2.69.160", _log.AllText);
        }
    }

    // Building the documents runs before the conditional update: when it fails the slot stays Pending, so the
    // launcher's retry of this 500 can still succeed.
    [Test]
    public async Task BuildingTheResultFails_500_AndTheSlotStaysPending()
    {
        _geo.Setup(g => g.Lookup(It.IsAny<IEnumerable<string>>())).Returns(new GeoThatFailsOnRead());

        var result = await _service.SubmitAsync(Id, Player, _token, Body(Full()), SocketIp);

        Assert.AreEqual(500, result.StatusCode);
        Assert.AreEqual("""{"error":"internal_error"}""", BodyOf(result));
        VerifyNoConditionalUpdate();
        VerifyNothingWritten();
        Assert.AreEqual(1, _log.Count(LogLevel.Error));
        StringAssert.Contains(Id, _log.AllText);
        StringAssert.Contains("run-1", _log.AllText);
        StringAssert.Contains(Player, _log.AllText);
        StringAssert.DoesNotContain(_token, _log.AllText);
    }

    /// <summary>Lists no entries (so the IP-info rows are built) but throws when ResultBuilder.Build looks an IP up.</summary>
    private sealed class GeoThatFailsOnRead : IReadOnlyDictionary<string, ClientGeo>
    {
        public ClientGeo this[string key] => throw new InvalidOperationException("build");
        public IEnumerable<string> Keys => [];
        public IEnumerable<ClientGeo> Values => [];
        public int Count => 0;
        public bool ContainsKey(string key) => throw new InvalidOperationException("build");
        public bool TryGetValue(string key, out ClientGeo value) => throw new InvalidOperationException("build");
        public IEnumerator<KeyValuePair<string, ClientGeo>> GetEnumerator() => Enumerable.Empty<KeyValuePair<string, ClientGeo>>().GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    // The update may have been applied although its reply was lost: the retry then gets 409. That must be in the log.
    [Test]
    public async Task TheConditionalUpdateThrows_500_LoggedWithMeasurementRunAndBattleTag()
    {
        _assignments.Setup(a => a.TrySubmitAsync(Id, Player, Now)).ThrowsAsync(new TimeoutException("mongo"));

        var result = await _service.SubmitAsync(Id, Player, _token, Body(Full()), SocketIp);

        Assert.AreEqual(500, result.StatusCode);
        Assert.AreEqual("""{"error":"internal_error"}""", BodyOf(result));
        VerifyNothingWritten();
        Assert.AreEqual(1, _log.Count(LogLevel.Error));
        StringAssert.Contains(Id, _log.AllText);
        StringAssert.Contains("run-1", _log.AllText);
        StringAssert.Contains(Player, _log.AllText);
        StringAssert.DoesNotContain(_token, _log.AllText);
    }

    // The IP info is a cache of enrichment data; the result itself is stored.
    [Test]
    public async Task IpInfoFailureAfterTheInsert_Still201_AndLogged()
    {
        TheConditionalUpdateSucceeds();
        _results.Setup(r => r.UpsertIpInfoAsync(It.IsAny<IReadOnlyCollection<NetworkDiagnosticIpInfo>>())).ThrowsAsync(new TimeoutException("mongo"));

        var result = await _service.SubmitAsync(Id, Player, _token, Body(Full()), SocketIp);

        Assert.AreEqual(201, result.StatusCode);
        Assert.IsNotNull(_built);
        _runs.Verify(r => r.IncrementCountersAsync("run-1", 0, 0, 1, 0), Times.Once);
        Assert.AreEqual(1, _log.Count(LogLevel.Error));
        StringAssert.Contains(Id, _log.AllText);
    }

    // P5: without a reported public_ipv4 the peer's public IPv4 is the client IP; without either it stays null.
    [TestCase(SocketIp, SocketIp)]
    [TestCase(null, null)]
    public async Task NoReportedPublicIp_FallsBackToTheSocketIpv4(string remoteIp, string expected)
    {
        TheConditionalUpdateSucceeds();
        List<string> lookedUp = null;
        _geo.Setup(g => g.Lookup(It.IsAny<IEnumerable<string>>()))
            .Callback<IEnumerable<string>>(ips => lookedUp = ips.ToList())
            .Returns(new Dictionary<string, ClientGeo>());

        var result = await _service.SubmitAsync(Id, Player, _token, Body(Full() with { PublicIpv4 = null }), remoteIp);

        Assert.AreEqual(201, result.StatusCode);
        Assert.AreEqual(expected, _built.Measurement.ClientIp);
        Assert.IsNull(_built.Measurement.PublicIpv4);
        Assert.AreEqual(expected != null, lookedUp.Contains(SocketIp));
        CollectionAssert.DoesNotContain(lookedUp, null);
    }

    public static IEnumerable<TestCaseData> SparseSubmissions()
    {
        const string id = SubmissionFixtures.MeasurementId;
        yield return new TestCaseData($$"""{"measurement_id":"{{id}}"}""", 0, 0)
            .SetName("A12_OnlyTheMeasurementId");
        yield return new TestCaseData($$"""
            {"measurement_id":"{{id}}","launcher_version":null,"os":null,"attempt_started_at":null,"window_started_at":null,
             "window_ended_at":null,"public_ipv4":null,"default_gateway":null,"pacing":null,"system":null,"flo_snapshot":null,
             "targets":[{"target_key":"gateway","label":null,"paths":[{"path":"direct","route_label":null,"endpoints":null,
               "probes":[{"method":"IcmpMtr","outcome":"error","reason":null,"hops":null,"samples":null}]}]}]}
            """, 1, 1).SetName("A12_EveryOptionalNull");
        yield return new TestCaseData($$"""
            {"measurement_id":"{{id}}","system":{"cpu":null,"memory":null,"adapters":null,"wifi":null,"vpn":null,"antivirus":null},
             "targets":[{"target_key":"flo:12","paths":[{"path":"proxied","endpoints":{"icmp":null,"tcp":null,"udp":null},
               "probes":[{"method":"FloUdpPing","outcome":"timeout"},{"method":"TcpTraceroute","outcome":"unsupported"}]}]}]}
            """, 1, 2).SetName("A12_OptionalsAbsent_ProbesWithNeitherHopsNorSamples");
        yield return new TestCaseData($$"""
            {"measurement_id":"{{id}}","future_field":{"nested":[1,2,3]},"os":{"family":"linux","kernel":"6.8"},
             "system":{"gpu":{"model":"x"},"wifi":{"reason":"a_reason_this_backend_has_never_seen","ssid_hash":"x"} },
             "targets":[{"target_key":"baseline:new","colour":"blue","paths":[{"path":"direct","hint":7,"endpoints":{"icmp":"9.9.9.9","sctp":"x"},
               "probes":[{"method":"QuicPing","outcome":"partial","reason":"a_reason_this_backend_has_never_seen","extra":true,
                 "hops":[{"ttl":1,"ip":null,"asn_hint":5,"samples":[[0,null]]}]}]}]}]}
            """, 1, 1).SetName("A12_UnknownPropertiesReasonMethodAndOutcome");
        yield return new TestCaseData(SubmissionFixtures.LauncherProducedJson, 2, 4).SetName("A12_TheLaunchersOwnSerializerOutput");
    }

    // A12 / P9: a 400 is terminal for the launcher, so a sparse but valid body must end in 201 and stored documents.
    [TestCaseSource(nameof(SparseSubmissions))]
    public async Task SparseButValidSubmission_201_AndStored(string json, int targets, int observations)
    {
        TheConditionalUpdateSucceeds();
        _geo.Setup(g => g.Lookup(It.IsAny<IEnumerable<string>>())).Returns(new Dictionary<string, ClientGeo>());

        var result = await _service.SubmitAsync(Id, Player, _token, Body(SubmissionFixtures.Parse(json)), null);

        Assert.AreEqual(201, result.StatusCode, BodyOf(result));
        Assert.AreEqual(Id, _built.Measurement.Id);
        Assert.AreEqual(Player, _built.Measurement.BattleTag);
        Assert.AreEqual(targets, _built.Measurement.Targets.Count);
        Assert.AreEqual(Id, _built.RawSeries.Id);
        Assert.AreEqual(observations, _built.Observations.Count);
        _results.Verify(r => r.InsertResultAsync(It.IsAny<BuiltResult>()), Times.Once);
        _runs.Verify(r => r.IncrementCountersAsync("run-1", 0, 0, 1, 0), Times.Once);
        Assert.AreEqual(0, _log.Count(LogLevel.Error));
    }
}

[TestFixture]
public class NetworkDiagnosticsSubmissionRegistrationTests
{
    // The submission service receives the measurement token (A10): it must be the plain class, never a Castle
    // tracing proxy that would record its arguments.
    [Test]
    public void AddNetworkDiagnostics_ResolvesThePlainSubmissionService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNetworkDiagnostics();
        services.AddSingleton(new Mock<INetworkDiagnosticsRunRepository>().Object);
        services.AddSingleton(new Mock<INetworkDiagnosticsAssignmentRepository>().Object);
        services.AddSingleton(new Mock<INetworkDiagnosticsResultRepository>().Object);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var service = provider.GetRequiredService<INetworkDiagnosticsSubmissionService>();

        Assert.AreEqual(typeof(NetworkDiagnosticsSubmissionService), service.GetType());
    }
}
