#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using W3ChampionsStatisticService.LagReports;
using W3ChampionsStatisticService.PlayerMatchTelemetry;
using PlayerMatchTelemetryDoc = W3ChampionsStatisticService.PlayerMatchTelemetry.PlayerMatchTelemetry;

namespace WC3ChampionsStatisticService.Tests.PlayerMatchTelemetry;

/// <summary>
/// The fields flo's client (transportStats, clientVersion) and launcher-e (routing,
/// launcherVersion) add to the telemetry submission.
/// </summary>
[TestFixture]
public class PlayerMatchTelemetryTransportStatsTests
{
    private static readonly JsonSerializerOptions WebDefaults = new(JsonSerializerDefaults.Web);

    private static string Payload(string extra) => $$"""
        {
          "gameId": 12345,
          "matchWallStart": "2026-05-21T12:00:00Z",
          "gameLengthMs": 10000,
          "crashedAt": null,
          "connectionType": "TCP",
          "disconnectEvents": [],
          "actionLatencyAggregate": { "sampleCount": 0, "p10Ms": 0, "p50Ms": 0, "p99Ms": 0, "p999Ms": 0, "meanMs": 0, "stddevMs": 0 },
          "actionLatencyTimeseries": { "gameTimeOffsetsMs": [], "meansMs": [], "sampleCounts": [] },
          "droppedUnmatchedCount": 0
          {{extra}}
        }
        """;

    // Linux client: every array present. lostMax omitted as on Windows.
    private const string FullExtra = """
        ,
        "clientVersion": "0.18.5",
        "launcherVersion": "2.4.0",
        "routing": { "proxyName": "brazil-north", "proxyAddress": "198.51.100.7:3552", "connectionKind": "proxied" },
        "transportStats": {
          "kind": "TCP",
          "gameTimeOffsetsMs": [0, 5000],
          "sampleCounts": [5, 4],
          "srttMaxMs": [40, 1200],
          "rttvarMaxMs": [3, 400],
          "retransDelta": [0, 2],
          "unackedMax": [1, 2],
          "rxBytesDelta": [900, 70000],
          "txBytesDelta": [800, 600],
          "stallSecs": [0, 1]
        }
        """;

    private static PlayerMatchTelemetrySubmissionDto Parse(string json) =>
        JsonSerializer.Deserialize<PlayerMatchTelemetrySubmissionDto>(json, WebDefaults)!;

    private static (bool ok, IList<ValidationResult> errors) Validate(object dto)
    {
        var errors = new List<ValidationResult>();
        var ok = Validator.TryValidateObject(dto, new ValidationContext(dto), errors, validateAllProperties: true);
        return (ok, errors);
    }

    private static TransportStatsDto Stats(int n, Func<int, ushort[]?>? optional = null) => new()
    {
        Kind = Transport.TCP,
        GameTimeOffsetsMs = Enumerable.Range(0, n).Select(i => (uint)(i * 5000)).ToArray(),
        SampleCounts = Enumerable.Repeat<byte>(5, n).ToArray(),
        SrttMaxMs = Enumerable.Repeat<ushort>(40, n).ToArray(),
        RttvarMaxMs = optional?.Invoke(n),
        RetransDelta = new ushort[n],
        RxBytesDelta = new uint[n],
        TxBytesDelta = new uint[n],
        StallSecs = new byte[n],
    };

    // ── Deserialization ───────────────────────────────────────────────

    [Test]
    public void NewFields_Deserialize()
    {
        var dto = Parse(Payload(FullExtra));

        Assert.That(dto.ClientVersion, Is.EqualTo("0.18.5"));
        Assert.That(dto.LauncherVersion, Is.EqualTo("2.4.0"));
        Assert.That(dto.Routing!.ProxyName, Is.EqualTo("brazil-north"));
        Assert.That(dto.Routing.ProxyAddress, Is.EqualTo("198.51.100.7:3552"));
        Assert.That(dto.Routing.ConnectionKind, Is.EqualTo("proxied"));

        var ts = dto.TransportStats!;
        Assert.That(ts.Kind, Is.EqualTo(Transport.TCP));
        Assert.That(ts.GameTimeOffsetsMs, Is.EqualTo(new uint[] { 0, 5000 }));
        Assert.That(ts.SampleCounts, Is.EqualTo(new byte[] { 5, 4 }));
        Assert.That(ts.SrttMaxMs, Is.EqualTo(new ushort[] { 40, 1200 }));
        Assert.That(ts.RttvarMaxMs, Is.EqualTo(new ushort[] { 3, 400 }));
        Assert.That(ts.LostMax, Is.Null);
        Assert.That(ts.UnackedMax, Is.EqualTo(new ushort[] { 1, 2 }));
        Assert.That(ts.RxBytesDelta, Is.EqualTo(new uint[] { 900, 70000 }));
        Assert.That(ts.StallSecs, Is.EqualTo(new byte[] { 0, 1 }));

        var (ok, errors) = Validate(dto);
        Assert.That(ok, Is.True, string.Join("; ", errors));
    }

    [Test]
    public void OldPayload_WithoutNewFields_IsAccepted()
    {
        var dto = Parse(Payload(""));

        Assert.That(dto.TransportStats, Is.Null);
        Assert.That(dto.ClientVersion, Is.Null);
        Assert.That(dto.LauncherVersion, Is.Null);
        Assert.That(dto.Routing, Is.Null);
        Assert.That(Validate(dto).ok, Is.True);
    }

    [Test]
    public void QuicKind_Deserializes()
    {
        var dto = Parse(Payload(FullExtra.Replace("\"kind\": \"TCP\"", "\"kind\": \"QUIC\"")));

        Assert.That(dto.TransportStats!.Kind, Is.EqualTo(Transport.QUIC));
    }

    // ── Validation ────────────────────────────────────────────────────

    [Test]
    public void MismatchedTransportArrays_FailValidation()
    {
        var dto = Parse(Payload("")) with { TransportStats = Stats(3) with { SrttMaxMs = new ushort[2] } };

        var (ok, errors) = Validate(dto);

        Assert.That(ok, Is.False);
        Assert.That(string.Join(";", errors), Does.Contain("transportStats"));
    }

    [Test]
    public void ExplicitNullRequiredArray_FailsValidationInsteadOfThrowingLater()
    {
        var dto = Parse(Payload(FullExtra.Replace("\"srttMaxMs\": [40, 1200]", "\"srttMaxMs\": null")));

        var (ok, errors) = Validate(dto);

        Assert.That(ok, Is.False);
        Assert.That(string.Join(";", errors), Does.Contain("SrttMaxMs"));
    }

    [Test]
    public void MismatchedOptionalTransportArray_FailsValidation()
    {
        var dto = Parse(Payload("")) with { TransportStats = Stats(3, _ => new ushort[4]) };

        Assert.That(Validate(dto).ok, Is.False);
    }

    [Test]
    public void TooManyTransportBuckets_FailValidation()
    {
        var dto = Parse(Payload("")) with { TransportStats = Stats(PlayerMatchTelemetrySubmissionDto.MaxTransportBuckets + 1) };

        Assert.That(Validate(dto).ok, Is.False);
    }

    [Test]
    public void TooLongStrings_FailValidation()
    {
        var baseDto = Parse(Payload(""));
        var longString = new string('x', 300);

        Assert.That(Validate(baseDto with { ClientVersion = longString }).ok, Is.False);
        Assert.That(Validate(baseDto with { LauncherVersion = longString }).ok, Is.False);
        Assert.That(Validate(baseDto with { Routing = new MatchTelemetryRoutingDto { ProxyName = longString } }).ok, Is.False);
    }

    // ── Storage and admin response ────────────────────────────────────

    [Test]
    public async Task Submit_StoresTransportStatsAsBinDataAndTheResponseDecodesThem()
    {
        var repo = new Mock<IPlayerMatchTelemetryRepository>();
        PlayerMatchTelemetryEntry? stored = null;
        repo.Setup(r => r.UpsertPlayerEntryAsync(It.IsAny<long>(), It.IsAny<DateTime>(), It.IsAny<PlayerMatchTelemetryEntry>(), It.IsAny<TimeSpan>()))
            .Callback<long, DateTime, PlayerMatchTelemetryEntry, TimeSpan>((_, _, e, _) => stored = e)
            .Returns(Task.CompletedTask);
        var controller = new PlayerMatchTelemetryController(repo.Object);

        await controller.Submit(Parse(Payload(FullExtra)), "Alice#1");

        Assert.That(stored, Is.Not.Null);
        Assert.That(stored!.ClientVersion, Is.EqualTo("0.18.5"));
        Assert.That(stored.LauncherVersion, Is.EqualTo("2.4.0"));
        Assert.That(stored.Routing!.ConnectionKind, Is.EqualTo("proxied"));
        var ts = stored.TransportStats!;
        Assert.That(ts.BucketCount, Is.EqualTo(2));
        Assert.That(ts.SrttMaxMs.Bytes, Is.EqualTo(new byte[] { 40, 0, 0xB0, 0x04 }), "uint16 little-endian");
        Assert.That(ts.LostMax, Is.Null, "an array the platform omits stays absent");

        var doc = new PlayerMatchTelemetryDoc { GameId = 12345, Players = [stored] };
        var entry = PlayerMatchTelemetryMapper.ToResponseDto(doc).Players.Single();
        Assert.That(entry.ClientVersion, Is.EqualTo("0.18.5"));
        Assert.That(entry.LauncherVersion, Is.EqualTo("2.4.0"));
        Assert.That(entry.Routing!.ProxyName, Is.EqualTo("brazil-north"));
        var rts = entry.TransportStats!;
        Assert.That(rts.Kind, Is.EqualTo(Transport.TCP));
        Assert.That(rts.GameTimeOffsetsMs, Is.EqualTo(new uint[] { 0, 5000 }));
        Assert.That(rts.SampleCounts, Is.EqualTo(new byte[] { 5, 4 }));
        Assert.That(rts.SrttMaxMs, Is.EqualTo(new ushort[] { 40, 1200 }));
        Assert.That(rts.RttvarMaxMs, Is.EqualTo(new ushort[] { 3, 400 }));
        Assert.That(rts.LostMax, Is.Null);
        Assert.That(rts.UnackedMax, Is.EqualTo(new ushort[] { 1, 2 }));
        Assert.That(rts.RetransDelta, Is.EqualTo(new ushort[] { 0, 2 }));
        Assert.That(rts.RxBytesDelta, Is.EqualTo(new uint[] { 900, 70000 }));
        Assert.That(rts.TxBytesDelta, Is.EqualTo(new uint[] { 800, 600 }));
        Assert.That(rts.StallSecs, Is.EqualTo(new byte[] { 0, 1 }));

        var json = JsonSerializer.Serialize(entry, WebDefaults);
        Assert.That(json, Does.Contain("\"stallSecs\":[0,1]"), "byte arrays go out as numbers, not base64");
        Assert.That(json, Does.Contain("\"sampleCounts\":[5,4]"));
    }

    [Test]
    public void ResponseForAnOldEntry_HasNullNewFields()
    {
        var doc = new PlayerMatchTelemetryDoc { GameId = 1, Players = [new PlayerMatchTelemetryEntry { BattleTag = "Old#1" }] };

        var entry = PlayerMatchTelemetryMapper.ToResponseDto(doc).Players.Single();

        Assert.That(entry.TransportStats, Is.Null);
        Assert.That(entry.ClientVersion, Is.Null);
        Assert.That(entry.Routing, Is.Null);
    }
}
