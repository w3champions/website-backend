using System.Text.Json;
using NUnit.Framework;
using W3ChampionsStatisticService.LagReports;

namespace WC3ChampionsStatisticService.Tests.LagReports;

/// <summary>
/// Pins the numeric contract for flo-stats ping samples.
///
/// Pure unit tests over a JsonElement — no WebSocket, no Mongo, no
/// IntegrationTestBase. ParsePingData is the only part of FloStatsService that can
/// be exercised without a live socket, and it is where the type disagreement below
/// surfaced.
/// </summary>
[TestFixture]
public class FloStatsPingParsingTests
{
    private static JsonElement Payload(string json) => JsonDocument.Parse(json).RootElement;

    private static string SnapshotWith(string sample) =>
        $$"""
        {
          "game": { "players": [ { "id": 1, "name": "Player1#1234" } ] },
          "stats": { "ping": [ { "time": 1000, "data": [ {{sample}} ] } ] }
        }
        """;

    [Test]
    public void AFractionalAverageIsRoundedRatherThanThrowing()
    {
        // Regression: flo's Ping.avg is an f32, so it arrives as a GraphQL Float and
        // serialises as "42.7" — and JsonElement.GetInt32() throws FormatException on
        // that. The throw escaped ParsePingData, was swallowed by the caller's catch,
        // and an empty ping array was then persisted, which the "already populated"
        // guard treated as done and never retried.
        var ping = FloStatsService.ParsePingData(Payload(
            SnapshotWith("""{ "playerId": 1, "min": 30, "max": 55, "avg": 42.7 }""")));

        Assert.That(ping, Has.Count.EqualTo(1));
        Assert.That(ping[0].Samples, Has.Count.EqualTo(1));
        Assert.That(ping[0].Samples[0].Avg, Is.EqualTo(43));
        Assert.That(ping[0].Samples[0].Min, Is.EqualTo(30));
        Assert.That(ping[0].Samples[0].Max, Is.EqualTo(55));
    }

    [Test]
    public void AnIntegralAverageStillParses()
    {
        // The one that shows the bug was total rather than occasional: serde renders an
        // integral f32 as "42.0", which GetInt32() rejects just as hard as "42.7". Since
        // avg is always a Float, every parse was failing.
        var ping = FloStatsService.ParsePingData(Payload(
            SnapshotWith("""{ "playerId": 1, "min": 30, "max": 55, "avg": 42.0 }""")));

        Assert.That(ping[0].Samples[0].Avg, Is.EqualTo(42));
    }

    [Test]
    public void AMissingSampleFieldIsNullRatherThanZero()
    {
        // Null and zero are different claims about latency; a missing field must not
        // read as a perfect ping.
        var ping = FloStatsService.ParsePingData(Payload(
            SnapshotWith("""{ "playerId": 1 }""")));

        Assert.That(ping[0].Samples[0].Avg, Is.Null);
        Assert.That(ping[0].Samples[0].Min, Is.Null);
        Assert.That(ping[0].Samples[0].Max, Is.Null);
    }

    [Test]
    public void PlayerNamesAreResolvedOntoTheSamples()
    {
        var ping = FloStatsService.ParsePingData(Payload(
            SnapshotWith("""{ "playerId": 1, "min": 30, "max": 55, "avg": 42.5 }""")));

        Assert.That(ping[0].PlayerId, Is.EqualTo(1));
        Assert.That(ping[0].PlayerName, Is.EqualTo("Player1#1234"));
    }
}
