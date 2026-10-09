using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using W3ChampionsStatisticService.LagReports;

namespace WC3ChampionsStatisticService.Tests.LagReports;

/// <summary>
/// Parsing of the flo-stats <c>GameSnapshotWithStats</c> payload. flo-stats encodes
/// <c>avg</c> as a JSON decimal, so integer-only parsing throws and drops the whole snapshot.
/// </summary>
[TestFixture]
public class FloStatsPingParsingTests
{
    // Shape of payload.data.gameUpdateEvents as flo-stats sends it.
    private const string Snapshot = """
        {
          "__typename": "GameSnapshotWithStats",
          "stats": {
            "ping": [
              { "time": 12.5, "data": [
                { "playerId": 1, "min": 80, "max": 95, "avg": 87.33 },
                { "playerId": 2, "min": 40, "max": 41, "avg": 40.0 }
              ] },
              { "time": 22.5, "data": [
                { "playerId": 1, "min": null, "max": 99, "avg": 88.5 },
                { "playerId": 2, "max": 45 }
              ] }
            ]
          },
          "game": { "players": [ { "id": 1, "name": "Alice#1234" }, { "id": 2, "name": "Bob#5678" } ] }
        }
        """;

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Test]
    public void ParsePingData_DecimalAvg_IsKeptAndIntegersAndNullsParse()
    {
        var result = FloStatsService.ParsePingData(Parse(Snapshot));

        Assert.AreEqual(2, result.Count);
        var alice = result.Single(p => p.PlayerId == 1);
        Assert.AreEqual("Alice#1234", alice.PlayerName);
        Assert.AreEqual(2, alice.Samples.Count);
        Assert.AreEqual(12.5, alice.Samples[0].Time);
        Assert.AreEqual(80, alice.Samples[0].Min);
        Assert.AreEqual(95, alice.Samples[0].Max);
        Assert.AreEqual(87.33, alice.Samples[0].Avg!.Value, 1e-9);
        Assert.IsNull(alice.Samples[1].Min);
        Assert.AreEqual(88.5, alice.Samples[1].Avg!.Value, 1e-9);

        var bob = result.Single(p => p.PlayerId == 2);
        Assert.AreEqual(40.0, bob.Samples[0].Avg!.Value, 1e-9);
        Assert.IsNull(bob.Samples[1].Min);
        Assert.AreEqual(45, bob.Samples[1].Max);
        Assert.IsNull(bob.Samples[1].Avg);
    }

    [Test]
    public void ParsePingData_DecimalMinMax_AreRounded()
    {
        var json = """
            { "stats": { "ping": [ { "time": 1, "data": [ { "playerId": 3, "min": 79.6, "max": 101.0, "avg": 90 } ] } ] } }
            """;

        var sample = FloStatsService.ParsePingData(Parse(json)).Single().Samples.Single();

        Assert.AreEqual(80, sample.Min);
        Assert.AreEqual(101, sample.Max);
        Assert.AreEqual(90.0, sample.Avg!.Value, 1e-9);
    }

    [Test]
    public void ParsePingData_NoPingStats_ReturnsEmpty()
    {
        var result = FloStatsService.ParsePingData(Parse("""{ "stats": { "ping": [] }, "game": { "players": [] } }"""));

        Assert.IsNotNull(result);
        Assert.IsEmpty(result);
    }

    [Test]
    public void ParsePlayers_MapsFloIdsToBattleTags()
    {
        var players = FloStatsService.ParsePlayers(Parse(Snapshot));

        Assert.That(players, Has.Count.EqualTo(2));
        Assert.That(players[1], Is.EqualTo("Alice#1234"));
        Assert.That(players[2], Is.EqualTo("Bob#5678"));
    }

    [Test]
    public void ParseOrNull_MalformedSnapshotGivesNullInsteadOfThrowing()
    {
        var malformed = Parse("""{ "stats": { "ping": [ { "time": 1, "data": [ { "min": 1 } ] } ] } }""");

        Assert.That(FloStatsService.ParseOrNull(malformed, FloStatsService.ParsePingData, 7), Is.Null);
        Assert.That(FloStatsService.ParseOrNull(Parse(Snapshot), FloStatsService.ParsePlayers, 7), Has.Count.EqualTo(2));
    }
}
