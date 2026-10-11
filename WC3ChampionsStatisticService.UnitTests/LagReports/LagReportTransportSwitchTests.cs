using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Mongo2Go;
using MongoDB.Driver;
using NUnit.Framework;
using W3ChampionsStatisticService.LagReports;

namespace WC3ChampionsStatisticService.Tests.LagReports;

/// <summary>
/// Wire contract, validation, mapping and persistence for the client-reported transport switch
/// (<c>diagnostics.transport_switches</c>): flo-client moved the game stream between TCP and
/// QUIC, or a reconnect came back over the other transport. Persistence uses an ISOLATED
/// in-memory mongod (Mongo2Go), NOT the shared remote Mongo of <c>IntegrationTestBase</c>.
/// </summary>
[TestFixture]
public class LagReportTransportSwitchTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset SwitchAt = DateTimeOffset.Parse("2026-10-11T18:40:00.5Z");

    // The exact shape flo-client produces and the launcher passes through untouched.
    private const string TransportSwitchJson = """
    {
      "game_id": 1001,
      "player_id": 1,
      "transport_switches": [
        {
          "timestamp": "2026-10-11T18:40:00.5Z",
          "game_time_offset": 192500,
          "from": "QUIC",
          "to": "TCP",
          "reason": "failover"
        }
      ]
    }
    """;

    private MongoDbRunner _runner;
    private LagReportRepository _repo;

    [SetUp]
    public void SetUp()
    {
        _runner = MongoDbRunner.Start();
        _repo = new LagReportRepository(new MongoClient(_runner.ConnectionString));
    }

    [TearDown]
    public void TearDown()
    {
        _runner.Dispose();
    }

    private static LagReportSubmissionDto CreateDto(int floGameId, params TransportSwitchDto[] switches) => new()
    {
        Diagnostics = new DiagnosticsDataDto
        {
            GameId = floGameId,
            PlayerId = 1,
            LagEvents = [],
            TargetMtr = [],
            AllServerBaselines = [],
            ReverseMtr = [],
            PingHistory = [],
            ConnectionEvents = [],
            TransportSwitches = [.. switches],
        },
        GameMetadata = new GameMetadataDto
        {
            GameId = floGameId + 4000,
            FloGameId = floGameId,
            GameName = "transport-switch-test-game",
            MapPath = "(2)EchoIsles.w3x",
        },
        ConnectionTopology = new ConnectionTopologyDto
        {
            ServerNodeId = 1,
            ServerNodeName = "EU West",
            ConnectionType = EConnectionType.Direct,
            ClientIp = "203.0.113.1",
            Transport = Transport.TCP,
        },
        IsExplicit = false,
        Categories = [],
        ConnectionIssueTags = [],
        FreeText = "",
        Annotations = [],
    };

    private static TransportSwitchDto CreateSwitch(string from = "QUIC", string to = "TCP", string reason = "failover") => new()
    {
        Timestamp = SwitchAt,
        GameTimeOffsetMs = 192500,
        From = from,
        To = to,
        Reason = reason,
    };

    private async Task<LagReport> SubmitAndFetch(LagReportSubmissionDto dto, string battleTag)
    {
        Assert.IsNull(LagReportController.ValidateSubmission(dto));
        var player = LagReportController.MapToPlayer(dto, battleTag);
        var template = new LagReport
        {
            GameId = dto.GameMetadata.GameId,
            FloGameId = dto.GameMetadata.FloGameId,
            GameName = dto.GameMetadata.GameName,
            MapPath = dto.GameMetadata.MapPath,
            ServerNodeId = dto.ConnectionTopology.ServerNodeId,
            ServerNodeName = dto.ConnectionTopology.ServerNodeName,
        };
        var reportId = await _repo.UpsertPlayerData(template.FloGameId, player, template);
        return await _repo.GetById(reportId);
    }

    // ── Wire contract ─────────────────────────────────────────────────

    [Test]
    public void Diagnostics_deserializes_transport_switches_from_client_payload()
    {
        var diag = JsonSerializer.Deserialize<DiagnosticsDataDto>(TransportSwitchJson, Web);

        Assert.That(diag!.TransportSwitches, Has.Count.EqualTo(1));
        var s = diag.TransportSwitches[0];
        Assert.That(s.Timestamp, Is.EqualTo(SwitchAt));
        Assert.That(s.GameTimeOffsetMs, Is.EqualTo(192500));
        Assert.That(s.From, Is.EqualTo("QUIC"));
        Assert.That(s.To, Is.EqualTo("TCP"));
        Assert.That(s.Reason, Is.EqualTo("failover"));
    }

    [Test]
    public void Diagnostics_WITHOUT_transport_switches_deserializes_to_empty_not_null()
    {
        // Older clients omit the field entirely.
        const string json = """{ "game_id": 1001, "player_id": 1, "connection_events": [] }""";

        var diag = JsonSerializer.Deserialize<DiagnosticsDataDto>(json, Web);

        Assert.That(diag!.TransportSwitches, Is.Not.Null);
        Assert.That(diag.TransportSwitches, Is.Empty);
    }

    [Test]
    public void Unknown_reason_and_transport_are_accepted_not_rejected()
    {
        // WHY these are strings: an unknown enum member would 400 the whole submission.
        const string json = """
        { "transport_switches": [ { "from": "QUIC", "to": "WEBRTC", "reason": "a_reason_this_build_never_heard_of" } ] }
        """;

        var diag = JsonSerializer.Deserialize<DiagnosticsDataDto>(json, Web);

        Assert.That(diag!.TransportSwitches[0].To, Is.EqualTo("WEBRTC"));
        Assert.That(diag.TransportSwitches[0].Reason, Is.EqualTo("a_reason_this_build_never_heard_of"));
    }

    // ── Validation ────────────────────────────────────────────────────

    [Test]
    public void Validate_TransportSwitches_IsValid()
    {
        Assert.IsNull(LagReportController.ValidateSubmission(CreateDto(1001, CreateSwitch())));
    }

    [Test]
    public void Validate_AtTheCap_IsValid_AndOneMore_IsNot()
    {
        var atCap = CreateDto(1001, Enumerable.Range(0, 200).Select(_ => CreateSwitch()).ToArray());
        Assert.IsNull(LagReportController.ValidateSubmission(atCap));

        var overCap = CreateDto(1001, Enumerable.Range(0, 201).Select(_ => CreateSwitch()).ToArray());
        Assert.AreEqual("Too many transport_switches", LagReportController.ValidateSubmission(overCap));
    }

    [TestCase("from")]
    [TestCase("to")]
    [TestCase("reason")]
    public void Validate_FieldTooLong_ReturnsError(string field)
    {
        var tooLong = new string('x', 501);
        var s = field switch
        {
            "from" => CreateSwitch(from: tooLong),
            "to" => CreateSwitch(to: tooLong),
            _ => CreateSwitch(reason: tooLong),
        };

        Assert.AreEqual("transport_switch field too long", LagReportController.ValidateSubmission(CreateDto(1001, s)));
    }

    [Test]
    public void Validate_NullTransportSwitches_DoesNotThrow()
    {
        var dto = CreateDto(1001);
        dto.Diagnostics.TransportSwitches = null;

        Assert.IsNull(LagReportController.ValidateSubmission(dto));
    }

    // ── Mapping ───────────────────────────────────────────────────────

    [Test]
    public void MapToPlayer_MapsTransportSwitches_InOrder()
    {
        var dto = CreateDto(1001, CreateSwitch(), CreateSwitch("TCP", "QUIC", "upgrade"));

        var switches = LagReportController.MapToPlayer(dto, "Switcher#1234").Diagnostics.TransportSwitches;

        Assert.That(switches.Select(s => (s.From, s.To, s.Reason)), Is.EqualTo(new[]
        {
            ("QUIC", "TCP", "failover"),
            ("TCP", "QUIC", "upgrade"),
        }));
        Assert.That(switches[0].Timestamp, Is.EqualTo(SwitchAt));
        Assert.That(switches[0].GameTimeOffsetMs, Is.EqualTo(192500));
    }

    [Test]
    public void MapToPlayer_NullTransportSwitches_ProducesEmptyList()
    {
        var dto = CreateDto(1001);
        dto.Diagnostics.TransportSwitches = null;

        var player = LagReportController.MapToPlayer(dto, "P#1");

        Assert.IsNotNull(player.Diagnostics.TransportSwitches);
        Assert.IsEmpty(player.Diagnostics.TransportSwitches);
    }

    [Test]
    public void MapToSummary_SurfacesTheCount_AndZeroWithoutDiagnostics()
    {
        var player = LagReportController.MapToPlayer(CreateDto(1001, CreateSwitch(), CreateSwitch()), "P#1");
        Assert.AreEqual(2, LagReportController.MapToSummary(player).TransportSwitchCount);

        player.Diagnostics.TransportSwitches = null;
        Assert.AreEqual(0, LagReportController.MapToSummary(player).TransportSwitchCount);

        player.Diagnostics = null;
        Assert.AreEqual(0, LagReportController.MapToSummary(player).TransportSwitchCount);
    }

    // ── Persistence ───────────────────────────────────────────────────

    [Test]
    public async Task Submission_With_TransportSwitches_RoundTrips_To_StoredDocument()
    {
        var report = await SubmitAndFetch(CreateDto(1001, CreateSwitch()), "Switcher#1234");

        var switches = report.Players[0].Diagnostics.TransportSwitches;
        Assert.That(switches, Has.Count.EqualTo(1));
        Assert.That(switches[0].Timestamp, Is.EqualTo(SwitchAt));
        Assert.That(switches[0].GameTimeOffsetMs, Is.EqualTo(192500));
        Assert.That((switches[0].From, switches[0].To, switches[0].Reason), Is.EqualTo(("QUIC", "TCP", "failover")));
    }

    [Test]
    public async Task Submission_WITHOUT_TransportSwitches_Still_Succeeds()
    {
        var report = await SubmitAndFetch(CreateDto(1002), "OldClient#1234");

        Assert.That(report.Players[0].Diagnostics.TransportSwitches, Is.Not.Null);
        Assert.That(report.Players[0].Diagnostics.TransportSwitches, Is.Empty);
    }

    [Test]
    public async Task GetReports_ListProjection_KeepsTransportSwitches()
    {
        await SubmitAndFetch(CreateDto(1003, CreateSwitch()), "Switcher#1234");

        var (items, _) = await _repo.GetReports(new LagReportQueryRequest());

        Assert.That(items[0].Players[0].Diagnostics.TransportSwitches, Has.Count.EqualTo(1),
            "the list shows the switch count, so the projection must keep them");
    }
}
