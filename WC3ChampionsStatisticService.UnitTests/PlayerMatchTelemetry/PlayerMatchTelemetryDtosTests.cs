using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using NUnit.Framework;
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
            BucketMs: 1000,
            MatchWallStart: new DateTime(2026, 5, 21, 12, 0, 0, DateTimeKind.Utc),
            GameLengthMs: 600_000,
            Crashed: false,
            ConnectionType: "QUIC",
            Disconnects: new DisconnectsDto(0, 0, 0),
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
        var huge = new uint[28_801];
        var means = new ushort[28_801];
        var counts = new byte[28_801];
        var v = MakeValid();
        var bad = v with
        {
            ActionLatencyTimeseries = new ActionLatencyTimeseriesDto(huge, means, counts)
        };
        var (ok, errors) = Validate(bad);
        Assert.That(ok, Is.False);
        Assert.That(string.Join(";", errors), Does.Contain("28800"));
    }

    [Test]
    public void Invalid_connection_type_fails_validation()
    {
        var v = MakeValid();
        var bad = v with { ConnectionType = "UDP" };
        var (ok, _) = Validate(bad);
        Assert.That(ok, Is.False);
    }

    [Test]
    public void Bucket_ms_out_of_range_fails_validation()
    {
        var v = MakeValid();
        var bad = v with { BucketMs = 50 };  // below 100
        var (ok, _) = Validate(bad);
        Assert.That(ok, Is.False);
    }
}
