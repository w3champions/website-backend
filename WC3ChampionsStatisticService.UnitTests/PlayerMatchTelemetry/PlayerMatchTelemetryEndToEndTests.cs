using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using W3ChampionsStatisticService.PlayerMatchTelemetry;

namespace WC3ChampionsStatisticService.Tests.PlayerMatchTelemetry;

[TestFixture]
public class PlayerMatchTelemetryEndToEndTests : IntegrationTestBase
{
    private PlayerMatchTelemetryRepository _repo = null!;
    private PlayerMatchTelemetryController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _repo = new PlayerMatchTelemetryRepository(MongoClient);
        _controller = new PlayerMatchTelemetryController(_repo);
    }

    private static PlayerMatchTelemetrySubmissionDto MakeSubmission(long gameId)
        => new(
            GameId: gameId,
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
    public async Task Post_then_get_round_trips_with_bindata_intact()
    {
        var sub = MakeSubmission(54321);
        var postResult = await _controller.Submit(sub, "Alice#1234");
        Assert.That(postResult, Is.InstanceOf<OkResult>());

        var getResult = await _controller.GetByGame(54321);
        Assert.That(getResult, Is.InstanceOf<OkObjectResult>());
        var doc = (PlayerMatchTelemetryResponseDto)((OkObjectResult)getResult).Value!;
        Assert.That(doc.GameId, Is.EqualTo(54321));
        Assert.That(doc.Players.Count, Is.EqualTo(1));

        var p = doc.Players[0];
        Assert.That(p.BattleTag, Is.EqualTo("Alice#1234"));
        Assert.That(p.BucketCount, Is.EqualTo(3));
        // BinData envelopes: base64 strings whose decoded length matches the
        // little-endian byte counts (3 × uint32 = 12, 3 × uint16 = 6, 3 × uint8 = 3).
        Assert.That(Convert.FromBase64String(p.GameTimeOffsetsMs.Binary.Base64).Length, Is.EqualTo(12));
        Assert.That(Convert.FromBase64String(p.MeansMs.Binary.Base64).Length, Is.EqualTo(6));
        Assert.That(p.SampleCounts.Binary.Base64, Is.EqualTo(Convert.ToBase64String(new byte[] { 5, 7, 6 })));
    }

    [Test]
    public async Task Two_players_same_game_id_merge_into_one_doc()
    {
        var sub = MakeSubmission(54322);
        await _controller.Submit(sub, "Alice#1234");
        await _controller.Submit(sub, "Bob#5678");
        var getResult = await _controller.GetByGame(54322);
        var doc = (PlayerMatchTelemetryResponseDto)((OkObjectResult)getResult).Value!;
        Assert.That(doc.Players.Count, Is.EqualTo(2));
        Assert.That(doc.Players.Select(p => p.BattleTag).ToHashSet(),
            Is.EquivalentTo(new[] { "Alice#1234", "Bob#5678" }));
    }
}
