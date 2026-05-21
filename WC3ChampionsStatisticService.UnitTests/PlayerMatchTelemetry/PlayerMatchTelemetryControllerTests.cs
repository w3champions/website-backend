#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using W3ChampionsStatisticService.PlayerMatchTelemetry;
using PlayerMatchTelemetryDoc = W3ChampionsStatisticService.PlayerMatchTelemetry.PlayerMatchTelemetry;

namespace WC3ChampionsStatisticService.Tests.PlayerMatchTelemetry;

[TestFixture]
public class PlayerMatchTelemetryControllerTests
{
    private Mock<IPlayerMatchTelemetryRepository> _repo = null!;
    private PlayerMatchTelemetryController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _repo = new Mock<IPlayerMatchTelemetryRepository>();
        _controller = new PlayerMatchTelemetryController(_repo.Object);
    }

    private static PlayerMatchTelemetrySubmissionDto MakeSubmission(int buckets = 3)
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
                Enumerable.Range(0, buckets).Select(i => (uint)(i * 1000)).ToArray(),
                Enumerable.Repeat<ushort>(30, buckets).ToArray(),
                Enumerable.Repeat<byte>(5, buckets).ToArray()),
            DroppedUnmatchedCount: 0);

    [Test]
    public async Task Submit_calls_repository_with_authenticated_battletag()
    {
        var result = await _controller.Submit(MakeSubmission(), "Alice#1234");
        Assert.That(result, Is.InstanceOf<OkResult>());
        _repo.Verify(r => r.UpsertPlayerEntryAsync(
            12345L,
            It.IsAny<DateTime>(),
            1000,
            It.Is<PlayerMatchTelemetryEntry>(e => e.BattleTag == "Alice#1234" && e.BucketCount == 3),
            It.IsAny<TimeSpan>()),
            Times.Once);
    }

    [Test]
    public async Task GetByGame_returns_404_when_missing()
    {
        _repo.Setup(r => r.GetByGameIdAsync(99)).ReturnsAsync((PlayerMatchTelemetryDoc?)null);
        var result = await _controller.GetByGame(99);
        Assert.That(result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public async Task GetByGame_returns_doc_when_present()
    {
        // The controller now projects the domain model to a response DTO so
        // System.Text.Json can serialize BsonBinaryData fields (the raw domain
        // model would 500). Verify identity-by-projection on GameId.
        var doc = new PlayerMatchTelemetryDoc { GameId = 99 };
        _repo.Setup(r => r.GetByGameIdAsync(99)).ReturnsAsync(doc);
        var result = await _controller.GetByGame(99);
        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var value = ((OkObjectResult)result).Value;
        Assert.That(value, Is.InstanceOf<PlayerMatchTelemetryResponseDto>());
        Assert.That(((PlayerMatchTelemetryResponseDto)value!).GameId, Is.EqualTo(99));
    }
}
