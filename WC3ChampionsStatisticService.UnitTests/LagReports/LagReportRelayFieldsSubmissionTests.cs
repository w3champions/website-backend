using System.Text.Json;
using NUnit.Framework;
using W3ChampionsStatisticService.LagReports;

namespace WC3ChampionsStatisticService.Tests.LagReports;

/// <summary>The submission fields the relay-telemetry fetch depends on: flo player id and client version.</summary>
[TestFixture]
public class LagReportRelayFieldsSubmissionTests
{
    private static readonly JsonSerializerOptions WebDefaults = new(JsonSerializerDefaults.Web);

    private static string Payload(string diagnosticsExtra) => $$"""
        {
          "diagnostics": { "game_id": 1001, "player_id": 7, "lag_events": [] {{diagnosticsExtra}} },
          "game_metadata": { "game_id": 5001, "flo_game_id": 1001, "map_path": "m", "game_name": "g" },
          "connection_topology": { "server_node_id": 1, "server_node_name": "EU", "connection_type": "Direct" },
          "is_explicit": false
        }
        """;

    private static LagReportSubmissionDto Parse(string json) =>
        JsonSerializer.Deserialize<LagReportSubmissionDto>(json, WebDefaults);

    [Test]
    public void ClientVersion_IsReadAndStored()
    {
        var dto = Parse(Payload(""", "client_version": "0.18.5" """));

        var player = LagReportController.MapToPlayer(dto, "P#1");

        Assert.That(dto.Diagnostics.ClientVersion, Is.EqualTo("0.18.5"));
        Assert.That(player.Diagnostics.ClientVersion, Is.EqualTo("0.18.5"));
    }

    [Test]
    public void OldPayloadWithoutClientVersion_StillMaps()
    {
        var dto = Parse(Payload(""));

        Assert.That(LagReportController.ValidateSubmission(dto), Is.Null);
        var player = LagReportController.MapToPlayer(dto, "P#1");

        Assert.That(player.Diagnostics.ClientVersion, Is.Null);
    }

    [Test]
    public void FloPlayerId_ComesFromTheDiagnosticsPlayerId()
    {
        var player = LagReportController.MapToPlayer(Parse(Payload("")), "P#1");

        Assert.That(player.FloPlayerId, Is.EqualTo(7));
        Assert.That(player.RelayChain, Is.Null);
    }

    [Test]
    public void FloPlayerId_IsNullWhenTheLauncherSentNone()
    {
        var dto = Parse(Payload(""));
        dto.Diagnostics.PlayerId = 0;

        Assert.That(LagReportController.MapToPlayer(dto, "P#1").FloPlayerId, Is.Null);
    }

    [Test]
    public void ClientVersion_TooLongIsRejected()
    {
        var dto = Parse(Payload(""));
        dto.Diagnostics.ClientVersion = new string('9', 501);

        Assert.That(LagReportController.ValidateSubmission(dto), Is.EqualTo("client_version too long"));
    }

    [Test]
    public void ListSummary_LeavesTheRelayChainOut()
    {
        var summary = LagReportController.MapToSummary(new LagReportPlayer { BattleTag = "P#1", FloPlayerId = 7, RelayChain = new PlayerRelayChain() });
        var json = JsonSerializer.Serialize(summary, WebDefaults);

        Assert.That(json, Does.Not.Contain("relayChain"));
    }
}
