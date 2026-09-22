using System;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using W3ChampionsStatisticService.LagReports;

namespace WC3ChampionsStatisticService.Tests.LagReports;

/// <summary>
/// Wire contract, validation and DTO→model mapping for the node-reported host stall
/// (<c>diagnostics.host_stalls</c>). The launcher submits it as a snake_case array; the
/// controller maps it onto <see cref="PlayerDiagnostics.HostStalls"/>.
/// Persistence coverage lives in <c>LagReportHostStallRepositoryTests</c>.
/// </summary>
[TestFixture]
public class LagReportHostStallTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    // The exact payload shape the launcher POSTs, so the property names are pinned here
    // and not only in the C# attributes.
    private const string HostStallJson = """
    {
      "game_id": 1001,
      "player_id": 1,
      "host_stalls": [
        {
          "timestamp": "2026-09-21T20:11:08.011758Z",
          "game_time_offset": 43762,
          "stall_ms": 7000,
          "players_total": 8,
          "players_flagged": 7,
          "outcome": "mass_lag_suppressed"
        }
      ]
    }
    """;

    private static LagReportSubmissionDto CreateValidDto() => new()
    {
        Diagnostics = new DiagnosticsDataDto
        {
            GameId = 1001,
            PlayerId = 1,
            LagEvents = [],
            TargetMtr = [],
            AllServerBaselines = [],
            ReverseMtr = [],
            PingHistory = [],
            ConnectionEvents = [],
            HostStalls = [CreateHostStall()],
        },
        GameMetadata = new GameMetadataDto
        {
            GameId = 5001,
            FloGameId = 1001,
            GameName = "host-stall-test-game",
            MapPath = "(2)EchoIsles.w3x",
        },
        ConnectionTopology = new ConnectionTopologyDto
        {
            ServerNodeId = 1,
            ServerNodeName = "EU West",
            ConnectionType = EConnectionType.Direct,
            ClientIp = "203.0.113.1",
        },
        IsExplicit = false,
        Categories = [],
        ConnectionIssueTags = [],
        FreeText = "",
        Annotations = [],
    };

    private static HostStallDto CreateHostStall(string outcome = "mass_lag_suppressed") => new()
    {
        Timestamp = DateTimeOffset.Parse("2026-09-21T20:11:08.011758Z"),
        GameTimeOffsetMs = 43762,
        StallMs = 7000,
        PlayersTotal = 8,
        PlayersFlagged = 7,
        Outcome = outcome,
    };

    // ── Wire contract ─────────────────────────────────────────────────

    [Test]
    public void Diagnostics_deserializes_host_stalls_from_launcher_payload()
    {
        var diag = JsonSerializer.Deserialize<DiagnosticsDataDto>(HostStallJson, Web);

        Assert.That(diag!.HostStalls, Has.Count.EqualTo(1));
        var stall = diag.HostStalls[0];
        Assert.That(stall.Timestamp, Is.EqualTo(DateTimeOffset.Parse("2026-09-21T20:11:08.011758Z")));
        Assert.That(stall.GameTimeOffsetMs, Is.EqualTo(43762));
        Assert.That(stall.StallMs, Is.EqualTo(7000));
        Assert.That(stall.PlayersTotal, Is.EqualTo(8));
        Assert.That(stall.PlayersFlagged, Is.EqualTo(7));
        Assert.That(stall.Outcome, Is.EqualTo("mass_lag_suppressed"));
    }

    [Test]
    public void Diagnostics_WITHOUT_host_stalls_deserializes_to_empty_not_null()
    {
        // Older launchers omit the field entirely. That must keep working — it is the
        // compatibility guarantee for every client that has not shipped the node signal yet.
        const string json = """
        {
          "game_id": 1001,
          "player_id": 1,
          "lag_events": [],
          "connection_events": []
        }
        """;

        var diag = JsonSerializer.Deserialize<DiagnosticsDataDto>(json, Web);

        Assert.That(diag!.HostStalls, Is.Not.Null);
        Assert.That(diag.HostStalls, Is.Empty);
    }

    [Test]
    public void Outcome_is_a_string_so_an_unrecognised_value_is_accepted_not_rejected()
    {
        // This is WHY outcome is not a C# enum. JsonStringEnumConverter throws on an
        // unknown member, and [FromBody] turns that into a 400 that drops the entire
        // submission — a newer node emitting a new outcome would destroy whole reports.
        // As a string, an unknown value binds and is carried through verbatim.
        const string json = """
        {
          "host_stalls": [
            { "stall_ms": 1234, "outcome": "some_outcome_this_build_has_never_heard_of" }
          ]
        }
        """;

        var diag = JsonSerializer.Deserialize<DiagnosticsDataDto>(json, Web);

        Assert.That(diag!.HostStalls, Has.Count.EqualTo(1));
        Assert.That(diag.HostStalls[0].Outcome, Is.EqualTo("some_outcome_this_build_has_never_heard_of"));
    }

    // ── Validation ────────────────────────────────────────────────────

    [Test]
    public void Validate_HostStalls_IsValid()
    {
        Assert.IsNull(LagReportController.ValidateSubmission(CreateValidDto()));
    }

    [Test]
    public void Validate_TooManyHostStalls_ReturnsError()
    {
        var dto = CreateValidDto();
        dto.Diagnostics.HostStalls = Enumerable.Range(0, 201).Select(_ => CreateHostStall()).ToList();

        Assert.AreEqual("Too many host_stalls", LagReportController.ValidateSubmission(dto));
    }

    [Test]
    public void Validate_ExactlyAtHostStallCap_IsValid()
    {
        // Boundary: exactly 200 must pass (cap is > 200, not >= 200).
        var dto = CreateValidDto();
        dto.Diagnostics.HostStalls = Enumerable.Range(0, 200).Select(_ => CreateHostStall()).ToList();

        Assert.IsNull(LagReportController.ValidateSubmission(dto));
    }

    [Test]
    public void Validate_HostStallOutcomeTooLong_ReturnsError()
    {
        var dto = CreateValidDto();
        dto.Diagnostics.HostStalls = [CreateHostStall(new string('x', 501))];

        Assert.AreEqual("host_stall outcome too long", LagReportController.ValidateSubmission(dto));
    }

    [Test]
    public void Validate_NullHostStalls_DoesNotThrow()
    {
        var dto = CreateValidDto();
        dto.Diagnostics.HostStalls = null;

        Assert.IsNull(LagReportController.ValidateSubmission(dto));
    }

    // ── Mapping ───────────────────────────────────────────────────────

    [Test]
    public void MapToPlayer_MapsHostStalls()
    {
        var player = LagReportController.MapToPlayer(CreateValidDto(), "Stalled#1234");

        Assert.That(player.Diagnostics.HostStalls, Has.Count.EqualTo(1));
        var stall = player.Diagnostics.HostStalls[0];
        Assert.That(stall.Timestamp, Is.EqualTo(DateTimeOffset.Parse("2026-09-21T20:11:08.011758Z")));
        Assert.That(stall.GameTimeOffsetMs, Is.EqualTo(43762));
        Assert.That(stall.StallMs, Is.EqualTo(7000));
        Assert.That(stall.PlayersTotal, Is.EqualTo(8));
        Assert.That(stall.PlayersFlagged, Is.EqualTo(7));
        Assert.That(stall.Outcome, Is.EqualTo("mass_lag_suppressed"));
    }

    [Test]
    public void MapToPlayer_NullHostStalls_ProducesEmptyList()
    {
        var dto = CreateValidDto();
        dto.Diagnostics.HostStalls = null;

        var player = LagReportController.MapToPlayer(dto, "P#1");

        Assert.IsNotNull(player.Diagnostics.HostStalls);
        Assert.IsEmpty(player.Diagnostics.HostStalls);
    }

    [Test]
    public void MapToPlayer_CarriesUnrecognisedOutcomeVerbatim()
    {
        // Mapping must not normalise, validate or drop an outcome it does not recognise:
        // the whole point of the string is that a newer node's value survives intact.
        var dto = CreateValidDto();
        dto.Diagnostics.HostStalls = [CreateHostStall("future_outcome_v2")];

        var player = LagReportController.MapToPlayer(dto, "P#1");

        Assert.AreEqual("future_outcome_v2", player.Diagnostics.HostStalls[0].Outcome);
    }
}
