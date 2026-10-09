using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Moq;
using NUnit.Framework;
using W3ChampionsStatisticService.LagReports;
using W3ChampionsStatisticService.LagReports.FloControllerGrpc;
using static WC3ChampionsStatisticService.Tests.LagReports.RelayTelemetryTestData;

namespace WC3ChampionsStatisticService.Tests.LagReports;

[TestFixture]
public class RelayTelemetryServiceTests
{
    private const int FloGameId = 4242;
    private const string ReportId = "report-1";

    private Mock<IFloControllerRelayClient> _client;
    private Mock<ILagReportRelayStore> _store;
    private Mock<IFloStatsService> _floStats;
    private RelayTelemetryService _service;

    [SetUp]
    public void SetUp()
    {
        _client = new Mock<IFloControllerRelayClient>(MockBehavior.Strict);
        _store = new Mock<ILagReportRelayStore>(MockBehavior.Strict);
        _floStats = new Mock<IFloStatsService>();
        // flo names players by battletag; Player(id) uses "P{id}#1".
        _floStats.Setup(f => f.FetchGamePlayers(FloGameId))
            .ReturnsAsync(Enumerable.Range(1, 9).ToDictionary(i => i, i => $"P{i}#1"));
        _service = new RelayTelemetryService(_client.Object, _store.Object, _floStats.Object)
        {
            LateRetryDelays = [],
        };
    }

    private static IReadOnlyCollection<string> Tags(params string[] tags) =>
        It.Is<IReadOnlyCollection<string>>(t => t.OrderBy(x => x).SequenceEqual(tags.OrderBy(x => x)));

    private static LagReportPlayer Player(int? floPlayerId, PlayerRelayChain chain = null) => new()
    {
        BattleTag = $"P{floPlayerId}#1",
        FloPlayerId = floPlayerId,
        RelayChain = chain,
    };

    private void StoredReport(params LagReportPlayer[] players) =>
        _store.Setup(s => s.GetForRelayTelemetry(FloGameId))
            .ReturnsAsync(new LagReport { Id = ReportId, FloGameId = FloGameId, Players = [.. players] });

    private static GetGameRelayTelemetryReply ReplyWithNodeLeg(int playerId, int buckets, string status = RelayLegStatus.Measured)
    {
        var connection = new RelayConnection { ConnectedUnixMs = 1 };
        connection.Legs.Add(new RelayLeg
        {
            FromLabel = "client",
            ToLabel = "node-1",
            Status = status,
            Far = new RelaySeries
            {
                Role = "node_player",
                Kind = "tcp",
                FirstSeenUnixMs = 1,
                ClosedUnixMs = status == RelayLegStatus.PendingClose ? 0 : 2,
                Buckets = ByteString.CopyFrom(PackHealthy(buckets)),
                BucketCount = (uint)buckets,
            },
        });
        var reply = new GetGameRelayTelemetryReply { GameId = FloGameId, PlayerId = playerId };
        reply.Connections.Add(connection);
        return reply;
    }

    private void ExpectStore(int floPlayerId) =>
        _store.Setup(s => s.UpdatePlayerRelayChain(ReportId, floPlayerId, Tags($"P{floPlayerId}#1"), It.IsAny<PlayerRelayChain>())).Returns(Task.CompletedTask);

    // ── FetchForPlayer ────────────────────────────────────────────────

    [Test]
    public async Task FetchForPlayer_SuccessStoresTheMappedChainForThatPlayerOnly()
    {
        StoredReport(Player(5), Player(6));
        _client.Setup(c => c.GetGameRelayTelemetry(FloGameId, 5)).ReturnsAsync(ReplyWithNodeLeg(5, 12));
        ExpectStore(5);

        await _service.FetchForPlayer(FloGameId, 5);

        _store.Verify(s => s.UpdatePlayerRelayChain(ReportId, 5, Tags("P5#1"), It.Is<PlayerRelayChain>(c =>
            c.Connections.Count == 1 && c.Connections[0].Legs[0].Far.BucketCount == 12)), Times.Once);
        _store.Verify(s => s.UpdatePlayerRelayChain(ReportId, 6, Tags("P6#1"), It.IsAny<PlayerRelayChain>()), Times.Never);
    }

    [Test]
    public async Task FetchForPlayer_FailureLeavesTheStoredChainUntouched()
    {
        StoredReport(Player(5));
        _client.Setup(c => c.GetGameRelayTelemetry(FloGameId, 5)).ReturnsAsync((GetGameRelayTelemetryReply)null);

        await _service.FetchForPlayer(FloGameId, 5);

        _store.Verify(s => s.UpdatePlayerRelayChain(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<PlayerRelayChain>()), Times.Never);
    }

    [Test]
    public void FetchForPlayer_ClientExceptionIsContainedAndNothingIsStored()
    {
        StoredReport(Player(5));
        _client.Setup(c => c.GetGameRelayTelemetry(FloGameId, 5)).ThrowsAsync(new InvalidOperationException("boom"));

        Assert.DoesNotThrowAsync(() => _service.FetchForPlayer(FloGameId, 5));

        _store.Verify(s => s.UpdatePlayerRelayChain(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<PlayerRelayChain>()), Times.Never);
    }

    [Test]
    public async Task FetchForPlayer_FailureThenNextTriggerRetries()
    {
        StoredReport(Player(5));
        _client.SetupSequence(c => c.GetGameRelayTelemetry(FloGameId, 5))
            .ReturnsAsync((GetGameRelayTelemetryReply)null)
            .ReturnsAsync(ReplyWithNodeLeg(5, 3));
        ExpectStore(5);

        await _service.FetchForPlayer(FloGameId, 5);
        await _service.FetchForPlayer(FloGameId, 5);

        _client.Verify(c => c.GetGameRelayTelemetry(FloGameId, 5), Times.Exactly(2));
        _store.Verify(s => s.UpdatePlayerRelayChain(ReportId, 5, Tags("P5#1"), It.IsAny<PlayerRelayChain>()), Times.Once);
    }

    [Test]
    public async Task FetchForPlayer_FinalChainIsNotFetchedAgain()
    {
        StoredReport(Player(5, Chain(RelayedConnection(1, 10))));

        await _service.FetchForPlayer(FloGameId, 5);

        _client.Verify(c => c.GetGameRelayTelemetry(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task FetchForPlayer_ResubmissionAfterAFinalChainCopiesItWithoutFetching()
    {
        var final = Chain(RelayedConnection(1, 10));
        StoredReport(Player(5, final), Player(5));
        ExpectStore(5);

        await _service.FetchForPlayer(FloGameId, 5);

        _client.Verify(c => c.GetGameRelayTelemetry(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        _store.Verify(s => s.UpdatePlayerRelayChain(ReportId, 5, Tags("P5#1"), final), Times.Once);
    }

    [Test]
    public async Task FetchForPlayer_EmptyReplyIsNotStored()
    {
        StoredReport(Player(5));
        _client.Setup(c => c.GetGameRelayTelemetry(FloGameId, 5))
            .ReturnsAsync(new GetGameRelayTelemetryReply { GameId = FloGameId, PlayerId = 5 });

        await _service.FetchForPlayer(FloGameId, 5);

        _store.Verify(s => s.UpdatePlayerRelayChain(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<PlayerRelayChain>()), Times.Never);
    }

    [Test]
    public async Task FetchForPlayer_RefreshMergesIntoTheStoredChain()
    {
        StoredReport(Player(5, Chain(RelayedConnection(1, 30, RelayLegStatus.PendingClose, closedUnixMs: 0))));
        // The refresh only reaches the node leg (the relay is gone): the client leg must survive.
        _client.Setup(c => c.GetGameRelayTelemetry(FloGameId, 5)).ReturnsAsync(ReplyWithNodeLeg(5, 40));
        PlayerRelayChain stored = null;
        _store.Setup(s => s.UpdatePlayerRelayChain(ReportId, 5, Tags("P5#1"), It.IsAny<PlayerRelayChain>()))
            .Callback<string, int, IReadOnlyCollection<string>, PlayerRelayChain>((_, _, _, c) => stored = c)
            .Returns(Task.CompletedTask);

        await _service.FetchForPlayer(FloGameId, 5);

        Assert.That(stored, Is.Not.Null);
        Assert.That(stored.Connections[0].Legs, Has.Count.EqualTo(2), "different path: the connection with more data wins");
        Assert.That(stored.Connections[0].Legs[1].Far.BucketCount, Is.EqualTo(30));
    }

    [Test]
    public async Task FetchForPlayer_UnknownReportOrPlayerDoesNothing()
    {
        _store.Setup(s => s.GetForRelayTelemetry(FloGameId)).ReturnsAsync((LagReport)null);
        await _service.FetchForPlayer(FloGameId, 5);

        StoredReport(Player(6));
        await _service.FetchForPlayer(FloGameId, 5);

        _client.Verify(c => c.GetGameRelayTelemetry(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public void FetchForPlayer_StoreFailureIsContained()
    {
        _store.Setup(s => s.GetForRelayTelemetry(FloGameId)).ThrowsAsync(new TimeoutException("mongo"));

        Assert.DoesNotThrowAsync(() => _service.FetchForPlayer(FloGameId, 5));
    }

    [Test]
    public async Task FetchForPlayer_ConcurrentCallsShareOneFetch()
    {
        StoredReport(Player(5));
        var gate = new TaskCompletionSource<GetGameRelayTelemetryReply>();
        _client.Setup(c => c.GetGameRelayTelemetry(FloGameId, 5)).Returns(gate.Task);
        ExpectStore(5);

        var first = _service.FetchForPlayer(FloGameId, 5);
        var second = _service.FetchForPlayer(FloGameId, 5);
        gate.SetResult(ReplyWithNodeLeg(5, 1));
        await Task.WhenAll(first, second);

        _client.Verify(c => c.GetGameRelayTelemetry(FloGameId, 5), Times.Once);
    }

    // ── Reporter identity ─────────────────────────────────────────────

    [Test]
    public async Task FetchForPlayer_IdThatFloAssignsToAnotherPlayerIsIgnored()
    {
        // The launcher-submitted player_id 5 belongs to someone else in this game.
        _floStats.Setup(f => f.FetchGamePlayers(FloGameId))
            .ReturnsAsync(new Dictionary<int, string> { [5] = "Victim#1", [6] = "P5#1" });
        StoredReport(Player(5));

        await _service.FetchForPlayer(FloGameId, 5);

        _client.Verify(c => c.GetGameRelayTelemetry(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        _store.Verify(s => s.UpdatePlayerRelayChain(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<PlayerRelayChain>()), Times.Never);
    }

    [Test]
    public async Task FetchForPlayer_UnknownRosterSkipsAndRetriesLater()
    {
        _floStats.Setup(f => f.FetchGamePlayers(FloGameId)).ReturnsAsync((Dictionary<int, string>)null);
        StoredReport(Player(5));

        await _service.FetchForPlayer(FloGameId, 5);

        _client.Verify(c => c.GetGameRelayTelemetry(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task FetchForPlayer_ForgedEntryWithTheSameIdIsNotWritten()
    {
        var forged = new LagReportPlayer { BattleTag = "Mallory#1", FloPlayerId = 5 };
        StoredReport(forged, Player(5));
        _client.Setup(c => c.GetGameRelayTelemetry(FloGameId, 5)).ReturnsAsync(ReplyWithNodeLeg(5, 4));
        ExpectStore(5);

        await _service.FetchForPlayer(FloGameId, 5);

        _store.Verify(s => s.UpdatePlayerRelayChain(ReportId, 5, Tags("P5#1"), It.IsAny<PlayerRelayChain>()), Times.Once);
        _store.Verify(s => s.UpdatePlayerRelayChain(ReportId, 5, Tags("Mallory#1"), It.IsAny<PlayerRelayChain>()), Times.Never);
    }

    [Test]
    public async Task FetchForPlayer_BattleTagMatchIgnoresCase()
    {
        _floStats.Setup(f => f.FetchGamePlayers(FloGameId)).ReturnsAsync(new Dictionary<int, string> { [5] = "p5#1" });
        StoredReport(Player(5));
        _client.Setup(c => c.GetGameRelayTelemetry(FloGameId, 5)).ReturnsAsync(ReplyWithNodeLeg(5, 4));
        ExpectStore(5);

        await _service.FetchForPlayer(FloGameId, 5);

        _store.Verify(s => s.UpdatePlayerRelayChain(ReportId, 5, Tags("P5#1"), It.IsAny<PlayerRelayChain>()), Times.Once);
    }

    [Test]
    public async Task FetchForPlayer_UpdatesEveryCasingOfTheVerifiedBattleTag()
    {
        StoredReport(Player(5), new LagReportPlayer { BattleTag = "p5#1", FloPlayerId = 5 });
        _client.Setup(c => c.GetGameRelayTelemetry(FloGameId, 5)).ReturnsAsync(ReplyWithNodeLeg(5, 4));
        _store.Setup(s => s.UpdatePlayerRelayChain(ReportId, 5, Tags("P5#1", "p5#1"), It.IsAny<PlayerRelayChain>())).Returns(Task.CompletedTask);

        await _service.FetchForPlayer(FloGameId, 5);

        _store.Verify(s => s.UpdatePlayerRelayChain(ReportId, 5, Tags("P5#1", "p5#1"), It.IsAny<PlayerRelayChain>()), Times.Once);
    }

    // ── Load bounds and late submits ──────────────────────────────────

    [Test]
    public async Task FetchForPlayer_BoundsConcurrentControllerCalls()
    {
        var players = Enumerable.Range(1, RelayTelemetryService.MaxConcurrentFetches + 2).ToArray();
        StoredReport(players.Select(id => Player(id)).ToArray());
        var gate = new TaskCompletionSource<GetGameRelayTelemetryReply>();
        var running = 0;
        var peak = 0;
        foreach (var id in players)
        {
            var playerId = id;
            _client.Setup(c => c.GetGameRelayTelemetry(FloGameId, playerId)).Returns(async () =>
            {
                var now = Interlocked.Increment(ref running);
                InterlockedMax(ref peak, now);
                await gate.Task;
                Interlocked.Decrement(ref running);
                return ReplyWithNodeLeg(playerId, 1);
            });
            ExpectStore(playerId);
        }

        var all = players.Select(id => _service.FetchForPlayer(FloGameId, id)).ToArray();
        await Task.Yield();
        gate.SetResult(null);
        await Task.WhenAll(all);

        Assert.That(peak, Is.LessThanOrEqualTo(RelayTelemetryService.MaxConcurrentFetches));
        _client.Verify(c => c.GetGameRelayTelemetry(FloGameId, It.IsAny<int>()), Times.Exactly(players.Length));
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while ((seen = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, seen) != seen)
        {
        }
    }

    [Test]
    public async Task FetchForPlayer_LateSubmitRetriesWhileTheChainIsStillOpen()
    {
        _service.LateRetryDelays = [TimeSpan.Zero, TimeSpan.Zero];
        var stored = (PlayerRelayChain)null;
        _store.Setup(s => s.GetForRelayTelemetry(FloGameId))
            .ReturnsAsync(() => new LagReport { Id = ReportId, FloGameId = FloGameId, Players = [Player(5, stored)] });
        _client.SetupSequence(c => c.GetGameRelayTelemetry(FloGameId, 5))
            .ReturnsAsync(ReplyWithNodeLeg(5, 4, RelayLegStatus.PendingClose))
            .ReturnsAsync(ReplyWithNodeLeg(5, 6));
        _store.Setup(s => s.UpdatePlayerRelayChain(ReportId, 5, Tags("P5#1"), It.IsAny<PlayerRelayChain>()))
            .Callback<string, int, IReadOnlyCollection<string>, PlayerRelayChain>((_, _, _, c) => stored = c)
            .Returns(Task.CompletedTask);

        await _service.FetchForPlayer(FloGameId, 5);

        _client.Verify(c => c.GetGameRelayTelemetry(FloGameId, 5), Times.Exactly(2));
        Assert.That(stored.Connections[0].Legs[0].Status, Is.EqualTo(RelayLegStatus.Measured));
    }

    [Test]
    public async Task FetchForPlayer_LateRetriesAreBounded()
    {
        _service.LateRetryDelays = [TimeSpan.Zero, TimeSpan.Zero];
        StoredReport(Player(5));
        _client.Setup(c => c.GetGameRelayTelemetry(FloGameId, 5)).ReturnsAsync((GetGameRelayTelemetryReply)null);

        await _service.FetchForPlayer(FloGameId, 5);

        _client.Verify(c => c.GetGameRelayTelemetry(FloGameId, 5), Times.Exactly(3));
    }

    // ── RefreshOpen ───────────────────────────────────────────────────

    [Test]
    public async Task RefreshOpen_FailedRosterLookupIsNotRepeatedPerPlayer()
    {
        _floStats.Setup(f => f.FetchGamePlayers(FloGameId)).ReturnsAsync((Dictionary<int, string>)null);
        StoredReport(Player(5), Player(6), Player(7));

        await _service.RefreshOpen(FloGameId);

        _floStats.Verify(f => f.FetchGamePlayers(FloGameId), Times.Once);
        _client.Verify(c => c.GetGameRelayTelemetry(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task RefreshOpen_RefreshesOnlyPlayersWhoseChainIsMissingOrOpen()
    {
        var closed = Chain(RelayedConnection(1, 10));
        var pending = Chain(RelayedConnection(1, 10, RelayLegStatus.PendingClose, closedUnixMs: 0));
        // Player 5 left early and already has a final chain; 6 is pending; 7 has none; the
        // legacy entry without a flo player id cannot be queried.
        StoredReport(Player(5, closed), Player(6, pending), Player(7), Player(null));
        _client.Setup(c => c.GetGameRelayTelemetry(FloGameId, 6)).ReturnsAsync(ReplyWithNodeLeg(6, 20));
        _client.Setup(c => c.GetGameRelayTelemetry(FloGameId, 7)).ReturnsAsync(ReplyWithNodeLeg(7, 20));
        ExpectStore(6);
        ExpectStore(7);

        await _service.RefreshOpen(FloGameId);

        _client.Verify(c => c.GetGameRelayTelemetry(FloGameId, 5), Times.Never);
        _store.Verify(s => s.UpdatePlayerRelayChain(ReportId, 5, Tags("P5#1"), It.IsAny<PlayerRelayChain>()), Times.Never);
        _store.Verify(s => s.UpdatePlayerRelayChain(ReportId, 6, Tags("P6#1"), It.IsAny<PlayerRelayChain>()), Times.Once);
        _store.Verify(s => s.UpdatePlayerRelayChain(ReportId, 7, Tags("P7#1"), It.IsAny<PlayerRelayChain>()), Times.Once);
        _floStats.Verify(f => f.FetchGamePlayers(FloGameId), Times.Once, "one roster lookup per refresh, not per player");
    }

    [Test]
    public async Task RefreshOpen_OnePlayerFailingDoesNotStopTheOthers()
    {
        StoredReport(Player(6), Player(7));
        _client.Setup(c => c.GetGameRelayTelemetry(FloGameId, 6)).ThrowsAsync(new InvalidOperationException("boom"));
        _client.Setup(c => c.GetGameRelayTelemetry(FloGameId, 7)).ReturnsAsync(ReplyWithNodeLeg(7, 20));
        ExpectStore(7);

        await _service.RefreshOpen(FloGameId);

        _store.Verify(s => s.UpdatePlayerRelayChain(ReportId, 7, Tags("P7#1"), It.IsAny<PlayerRelayChain>()), Times.Once);
    }

    [Test]
    public async Task RefreshOpen_NoReportDoesNothing()
    {
        _store.Setup(s => s.GetForRelayTelemetry(FloGameId)).ReturnsAsync((LagReport)null);

        await _service.RefreshOpen(FloGameId);

        _client.Verify(c => c.GetGameRelayTelemetry(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }
}
