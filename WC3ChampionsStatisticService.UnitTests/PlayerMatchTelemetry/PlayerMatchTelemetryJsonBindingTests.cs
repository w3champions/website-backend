using System;
using System.Collections.Generic;
using System.Text.Json;
using NUnit.Framework;
using W3ChampionsStatisticService.LagReports;
using W3ChampionsStatisticService.PlayerMatchTelemetry;

namespace WC3ChampionsStatisticService.Tests.PlayerMatchTelemetry;

// Regression: launcher-e now sends camelCase JSON (Rust serde with
// #[serde(rename_all = "camelCase")]). ASP.NET Core's
// JsonSerializerDefaults.Web (used by [FromBody]) folds PascalCase C#
// property names ↔ camelCase wire keys automatically — no
// [JsonPropertyName] attributes required on the submission DTO graph.
[TestFixture]
public class PlayerMatchTelemetryJsonBindingTests
{
    // Mirrors the JsonSerializerOptions ASP.NET Core 8 uses for [FromBody] binding.
    private static readonly JsonSerializerOptions WebDefaults = new(JsonSerializerDefaults.Web);

    private const string CamelCasePayload = """
    {
      "gameId": 12345,
      "matchWallStart": "2026-05-21T12:00:00Z",
      "gameLengthMs": 600000,
      "crashedAt": null,
      "connectionType": "QUIC",
      "disconnectEvents": [],
      "actionLatencyAggregate": {
        "sampleCount": 100, "p10Ms": 20, "p50Ms": 40,
        "p99Ms": 200, "p999Ms": 400, "meanMs": 60, "stddevMs": 30
      },
      "actionLatencyTimeseries": {
        "gameTimeOffsetsMs": [0, 1000, 2000],
        "meansMs": [30, 42, 38],
        "sampleCounts": [5, 7, 6]
      },
      "droppedUnmatchedCount": 0
    }
    """;

    [Test]
    public void CamelCase_json_deserializes_into_submission_dto()
    {
        var dto = JsonSerializer.Deserialize<PlayerMatchTelemetrySubmissionDto>(CamelCasePayload, WebDefaults);

        Assert.That(dto, Is.Not.Null);
        Assert.That(dto!.GameId, Is.EqualTo(12345L));
        Assert.That(dto.MatchWallStart.Year, Is.EqualTo(2026));
        Assert.That(dto.GameLengthMs, Is.EqualTo(600_000u));
        Assert.That(dto.CrashedAt, Is.Null);
        Assert.That(dto.ConnectionType, Is.EqualTo(Transport.QUIC));
        Assert.That(dto.DroppedUnmatchedCount, Is.EqualTo(0u));

        Assert.That(dto.DisconnectEvents, Is.Not.Null);
        Assert.That(dto.DisconnectEvents, Is.Empty);

        Assert.That(dto.ActionLatencyAggregate, Is.Not.Null);
        Assert.That(dto.ActionLatencyAggregate.SampleCount, Is.EqualTo(100u));
        Assert.That(dto.ActionLatencyAggregate.P10Ms, Is.EqualTo((ushort)20));
        Assert.That(dto.ActionLatencyAggregate.P50Ms, Is.EqualTo((ushort)40));
        Assert.That(dto.ActionLatencyAggregate.P99Ms, Is.EqualTo((ushort)200));
        Assert.That(dto.ActionLatencyAggregate.P999Ms, Is.EqualTo((ushort)400));
        Assert.That(dto.ActionLatencyAggregate.MeanMs, Is.EqualTo((ushort)60));
        Assert.That(dto.ActionLatencyAggregate.StddevMs, Is.EqualTo((ushort)30));

        Assert.That(dto.ActionLatencyTimeseries, Is.Not.Null);
        Assert.That(dto.ActionLatencyTimeseries.GameTimeOffsetsMs, Is.EqualTo(new uint[] { 0, 1000, 2000 }));
        Assert.That(dto.ActionLatencyTimeseries.MeansMs, Is.EqualTo(new ushort[] { 30, 42, 38 }));
        Assert.That(dto.ActionLatencyTimeseries.SampleCounts, Is.EqualTo(new byte[] { 5, 7, 6 }));
    }

    [Test]
    public void Disconnect_events_deserialize_into_typed_records()
    {
        const string payload = """
        {
          "gameId": 1,
          "matchWallStart": "2026-05-21T12:00:00Z",
          "gameLengthMs": 600000,
          "crashedAt": "2026-05-21T12:05:00Z",
          "connectionType": "TCP",
          "disconnectEvents": [
            { "startedAt": "2026-05-21T12:03:00Z", "durationMs": 4500 },
            { "startedAt": "2026-05-21T12:04:00Z", "durationMs": 2100 }
          ],
          "actionLatencyAggregate": {
            "sampleCount": 100, "p10Ms": 20, "p50Ms": 40,
            "p99Ms": 200, "p999Ms": 400, "meanMs": 60, "stddevMs": 30
          },
          "actionLatencyTimeseries": {
            "gameTimeOffsetsMs": [0],
            "meansMs": [30],
            "sampleCounts": [5]
          },
          "droppedUnmatchedCount": 0
        }
        """;

        var dto = JsonSerializer.Deserialize<PlayerMatchTelemetrySubmissionDto>(payload, WebDefaults);

        Assert.That(dto, Is.Not.Null);
        Assert.That(dto!.CrashedAt, Is.Not.Null);
        Assert.That(dto.CrashedAt!.Value.Year, Is.EqualTo(2026));
        Assert.That(dto.DisconnectEvents.Count, Is.EqualTo(2));
        Assert.That(dto.DisconnectEvents[0].DurationMs, Is.EqualTo(4500u));
        Assert.That(dto.DisconnectEvents[1].DurationMs, Is.EqualTo(2100u));
    }

    [Test]
    public void Sample_counts_writes_as_number_array_not_base64()
    {
        var dto = new ActionLatencyTimeseriesDto(
            GameTimeOffsetsMs: new uint[] { 0, 1000 },
            MeansMs: new ushort[] { 30, 42 },
            SampleCounts: new byte[] { 5, 7, 6 });

        var json = JsonSerializer.Serialize(dto, WebDefaults);

        // Must serialize as JSON array of numbers, never as base64 ("BQcG"==[5,7,6]).
        Assert.That(json, Does.Contain("\"sampleCounts\":[5,7,6]"));
        Assert.That(json, Does.Not.Contain("BQcG"));
    }

    [Test]
    public void Sample_counts_rejects_out_of_range_byte_values()
    {
        const string bad = """
        {
          "gameTimeOffsetsMs": [0],
          "meansMs": [30],
          "sampleCounts": [300]
        }
        """;
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<ActionLatencyTimeseriesDto>(bad, WebDefaults));
    }

    [Test]
    public void Response_dto_serializes_with_camelcase_outer_and_plain_arrays()
    {
        // The GET /api/player-match-telemetry/by-game/{gameId} response uses
        // plain JSON number arrays (uint[]/ushort[]/byte[]) instead of MongoDB
        // Extended JSON BinData envelopes. The frontend consumes ordinary arrays
        // directly — no Base64 decode step required.
        //
        // The outer/entry property names must serialize as camelCase
        // (battleTag, gameId, sampleCounts, …) so the website's TypeScript
        // IPlayerMatchTelemetry interface (src/store/admin/playerMatchTelemetry/types.ts)
        // can consume the response via a typed cast. ASP.NET Core's HTTP pipeline
        // applies PropertyNamingPolicy = CamelCase by default — we replicate that
        // here via WebDefaults so the assertion matches the real wire shape.
        var dto = new PlayerMatchTelemetryResponseDto(
            GameId: 1L,
            MatchWallStart: DateTime.UtcNow,
            Players: new List<PlayerMatchTelemetryEntryResponseDto>
            {
                new(
                    BattleTag: "Alice#1234",
                    ConnectionType: Transport.QUIC,
                    GameLengthMs: 100,
                    CrashedAt: null,
                    DisconnectEvents: new List<DisconnectEvent>(),
                    ActionLatencyAggregate: new ActionLatencyAggregate { SampleCount = 1, P10Ms = 1, P50Ms = 1, P99Ms = 1, P999Ms = 1, MeanMs = 1, StddevMs = 1 },
                    BucketCount: 1,
                    GameTimeOffsetsMs: new uint[] { 0 },
                    MeansMs: new ushort[] { 5 },
                    SampleCounts: new byte[] { 1 },
                    DroppedUnmatchedCount: 0,
                    SubmittedAt: DateTime.UtcNow
                )
            },
            CreatedAt: DateTime.UtcNow,
            ExpiresAt: DateTime.UtcNow.AddDays(90)
        );

        var json = JsonSerializer.Serialize(dto, WebDefaults);

        // camelCase outer wire shape (matches website's IPlayerMatchTelemetry types).
        Assert.That(json, Does.Contain("\"gameId\":1"));
        Assert.That(json, Does.Contain("\"battleTag\":\"Alice#1234\""));
        Assert.That(json, Does.Contain("\"bucketCount\":1"));
        Assert.That(json, Does.Contain("\"gameTimeOffsetsMs\":[0]"));
        Assert.That(json, Does.Contain("\"meansMs\":[5]"));
        Assert.That(json, Does.Contain("\"sampleCounts\":[1]"));
        Assert.That(json, Does.Contain("\"droppedUnmatchedCount\":0"));
        Assert.That(json, Does.Contain("\"connectionType\":\"QUIC\""));

        // Nested aggregates also camelCase.
        Assert.That(json, Does.Contain("\"sampleCount\":1"));
        Assert.That(json, Does.Contain("\"p999Ms\":1"));

        // No BinData envelope leakage on the response wire shape.
        Assert.That(json, Does.Not.Contain("$binary"));
        Assert.That(json, Does.Not.Contain("\"base64\""));

        // No snake_case leakage.
        Assert.That(json, Does.Not.Contain("\"battle_tag\""));
        Assert.That(json, Does.Not.Contain("\"game_id\""));
        Assert.That(json, Does.Not.Contain("\"sample_counts\""));
        Assert.That(json, Does.Not.Contain("\"p999_ms\""));

        // BucketMs is dropped from the wire (hardcoded to 1 s flo-side).
        Assert.That(json, Does.Not.Contain("\"bucketMs\""));
    }
}
