using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using NUnit.Framework;
using W3ChampionsStatisticService.LagReports;
using W3ChampionsStatisticService.PlayerMatchTelemetry;

namespace WC3ChampionsStatisticService.Tests.PlayerMatchTelemetry;

[TestFixture]
public class PlayerMatchTelemetryDtosTests
{
    private static (bool ok, IList<ValidationResult> errors) Validate(object dto)
    {
        var ctx = new ValidationContext(dto);
        var errors = new List<ValidationResult>();
        var ok = Validator.TryValidateObject(dto, ctx, errors, validateAllProperties: true);
        return (ok, errors);
    }

    private static PlayerMatchTelemetrySubmissionDto MakeValid()
        => new(
            GameId: 12345,
            MatchWallStart: new DateTime(2026, 5, 21, 12, 0, 0, DateTimeKind.Utc),
            GameLengthMs: 600_000,
            CrashedAt: null,
            ConnectionType: Transport.QUIC,
            DisconnectEvents: new List<DisconnectEventDto>(),
            ActionLatencyAggregate: new ActionLatencyAggregateDto(100, 20, 40, 200, 400, 60, 30),
            ActionLatencyTimeseries: new ActionLatencyTimeseriesDto(
                new uint[] { 0, 1000, 2000 },
                new ushort[] { 30, 42, 38 },
                new byte[] { 5, 7, 6 }),
            DroppedUnmatchedCount: 0);

    [Test]
    public void Valid_dto_passes_validation()
    {
        var (ok, errors) = Validate(MakeValid());
        Assert.That(ok, Is.True, string.Join("; ", errors));
    }

    [Test]
    public void Mismatched_array_lengths_fail_validation()
    {
        var v = MakeValid();
        var bad = v with
        {
            ActionLatencyTimeseries = new ActionLatencyTimeseriesDto(
                new uint[] { 0, 1000 },
                new ushort[] { 30, 42, 38 },
                new byte[] { 5, 7, 6 })
        };
        var (ok, errors) = Validate(bad);
        Assert.That(ok, Is.False);
        Assert.That(string.Join(";", errors).ToLower(), Does.Contain("parallel arrays"));
    }

    [Test]
    public void Bucket_count_over_28800_fails_validation()
    {
        const int MaxTimeseriesBuckets = 28_800;
        var huge = new uint[MaxTimeseriesBuckets + 1];
        var means = new ushort[MaxTimeseriesBuckets + 1];
        var counts = new byte[MaxTimeseriesBuckets + 1];
        var v = MakeValid();
        var bad = v with
        {
            ActionLatencyTimeseries = new ActionLatencyTimeseriesDto(huge, means, counts)
        };
        var (ok, errors) = Validate(bad);
        Assert.That(ok, Is.False);
        Assert.That(string.Join(";", errors), Does.Contain(MaxTimeseriesBuckets.ToString()));
    }

    [Test]
    public void Invalid_connection_type_value_fails_validation()
    {
        // The Transport enum can be cast from an arbitrary int — DataAnnotations'
        // EnumDataType attribute catches values that aren't defined members.
        var v = MakeValid();
        var bad = v with { ConnectionType = (Transport)999 };
        var (ok, _) = Validate(bad);
        Assert.That(ok, Is.False);
    }

    [Test]
    public void Game_id_zero_fails_range_validation()
    {
        // [Range(1, long.MaxValue)] on GameId — repurposed from the dropped
        // BucketMs range check now that bucketMs is hardcoded to 1 s and gone
        // from the wire.
        var v = MakeValid();
        var bad = v with { GameId = 0 };
        var (ok, _) = Validate(bad);
        Assert.That(ok, Is.False);
    }

    [Test]
    public void Null_disconnect_events_fails_validation()
    {
        var v = MakeValid();
        var bad = v with { DisconnectEvents = null! };
        var (ok, _) = Validate(bad);
        Assert.That(ok, Is.False);
    }

    [Test]
    public void Null_action_latency_aggregate_fails_validation()
    {
        var v = MakeValid();
        var bad = v with { ActionLatencyAggregate = null! };
        var (ok, _) = Validate(bad);
        Assert.That(ok, Is.False);
    }
}
