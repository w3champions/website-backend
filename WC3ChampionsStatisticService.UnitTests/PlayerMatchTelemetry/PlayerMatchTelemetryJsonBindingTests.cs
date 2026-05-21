using System.Text.Json;
using NUnit.Framework;
using W3ChampionsStatisticService.PlayerMatchTelemetry;

namespace WC3ChampionsStatisticService.Tests.PlayerMatchTelemetry;

// Regression: launcher-e sends snake_case JSON (Rust serde default). ASP.NET
// Core's System.Text.Json defaults (JsonSerializerDefaults.Web) only fold
// PascalCase/camelCase property names — snake_case fields would silently
// deserialize to zero/null and trip [Range(1, …)] on GameId. Confirms every
// property on the submission DTO graph has a [JsonPropertyName] mapping.
[TestFixture]
public class PlayerMatchTelemetryJsonBindingTests
{
    // Mirrors the JsonSerializerOptions ASP.NET Core 8 uses for [FromBody] binding.
    private static readonly JsonSerializerOptions WebDefaults = new(JsonSerializerDefaults.Web);

    private const string SnakeCasePayload = """
    {
      "game_id": 12345,
      "bucket_ms": 1000,
      "match_wall_start": "2026-05-21T12:00:00Z",
      "game_length_ms": 600000,
      "crashed": false,
      "connection_type": "QUIC",
      "disconnects": { "count": 0, "total_duration_ms": 0, "mean_duration_ms": 0 },
      "action_latency_aggregate": {
        "sample_count": 100, "p10_ms": 20, "p50_ms": 40,
        "p99_ms": 200, "p999_ms": 400, "mean_ms": 60, "stddev_ms": 30
      },
      "action_latency_timeseries": {
        "game_time_offsets_ms": [0, 1000, 2000],
        "means_ms": [30, 42, 38],
        "sample_counts": [5, 7, 6]
      },
      "dropped_unmatched_count": 0
    }
    """;

    [Test]
    public void Snake_case_json_deserializes_into_submission_dto()
    {
        var dto = JsonSerializer.Deserialize<PlayerMatchTelemetrySubmissionDto>(SnakeCasePayload, WebDefaults);

        Assert.That(dto, Is.Not.Null);
        Assert.That(dto!.GameId, Is.EqualTo(12345L));
        Assert.That(dto.BucketMs, Is.EqualTo(1000));
        Assert.That(dto.MatchWallStart.Year, Is.EqualTo(2026));
        Assert.That(dto.GameLengthMs, Is.EqualTo(600_000u));
        Assert.That(dto.Crashed, Is.False);
        Assert.That(dto.ConnectionType, Is.EqualTo("QUIC"));
        Assert.That(dto.DroppedUnmatchedCount, Is.EqualTo(0u));

        Assert.That(dto.Disconnects, Is.Not.Null);
        Assert.That(dto.Disconnects.Count, Is.EqualTo(0u));
        Assert.That(dto.Disconnects.TotalDurationMs, Is.EqualTo(0u));
        Assert.That(dto.Disconnects.MeanDurationMs, Is.EqualTo(0u));

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
    public void Sample_counts_writes_as_number_array_not_base64()
    {
        var dto = new ActionLatencyTimeseriesDto(
            GameTimeOffsetsMs: new uint[] { 0, 1000 },
            MeansMs: new ushort[] { 30, 42 },
            SampleCounts: new byte[] { 5, 7, 6 });

        var json = JsonSerializer.Serialize(dto, WebDefaults);

        // Must serialize as JSON array of numbers, never as base64 ("BQcG"==[5,7,6]).
        Assert.That(json, Does.Contain("\"sample_counts\":[5,7,6]"));
        Assert.That(json, Does.Not.Contain("BQcG"));
    }

    [Test]
    public void Sample_counts_rejects_out_of_range_byte_values()
    {
        const string bad = """
        {
          "game_time_offsets_ms": [0],
          "means_ms": [30],
          "sample_counts": [300]
        }
        """;
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<ActionLatencyTimeseriesDto>(bad, WebDefaults));
    }
}
