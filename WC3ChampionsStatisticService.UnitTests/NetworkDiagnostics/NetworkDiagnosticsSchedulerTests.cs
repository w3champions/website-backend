using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using W3ChampionsStatisticService.Hubs;
using W3ChampionsStatisticService.NetworkDiagnostics;
using W3ChampionsStatisticService.NetworkDiagnostics.Delivery;
using W3ChampionsStatisticService.NetworkDiagnostics.Persistence;
using W3ChampionsStatisticService.NetworkDiagnostics.Runs;

namespace WC3ChampionsStatisticService.Tests.NetworkDiagnostics;

[TestFixture]
public class NetworkDiagnosticsSchedulerTests
{
    private static readonly DateTime Now = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

    private ConnectionMapping _connections;
    private Mock<INetworkDiagnosticsRunRepository> _runs;
    private Mock<INetworkDiagnosticsAssignmentRepository> _assignments;
    private Mock<INetworkDiagnosticsDelivery> _delivery;
    private NetworkDiagnosticsScheduler _scheduler;

    [SetUp]
    public void SetUp()
    {
        _connections = new ConnectionMapping();
        _runs = new Mock<INetworkDiagnosticsRunRepository>();
        _assignments = new Mock<INetworkDiagnosticsAssignmentRepository>();
        _delivery = new Mock<INetworkDiagnosticsDelivery>();
        _runs.Setup(r => r.ActiveFleetRunsAsync(Now)).ReturnsAsync([]);
        _assignments.Setup(a => a.DueUndeliveredAsync(It.IsAny<IReadOnlyCollection<string>>(), Now, NetworkDiagnosticsScheduler.PushBatchLimit)).ReturnsAsync([]);
        _assignments.Setup(a => a.ExpireOverdueAsync(Now)).ReturnsAsync(new Dictionary<string, int>());
        _scheduler = new NetworkDiagnosticsScheduler(_connections, _runs.Object, _assignments.Object, _delivery.Object,
            new FixedTimeProvider(new DateTimeOffset(Now)), new QueueRandom(0.0), NullLogger<NetworkDiagnosticsScheduler>.Instance);
    }

    private void Online(params string[] tags)
    {
        foreach (var tag in tags) _connections.Add("conn-" + tag, new WebSocketUser { BattleTag = tag, ConnectionId = "conn-" + tag });
    }

    private static NetworkDiagnosticRun CountRun(int count, int delivered) => new()
    {
        Id = "fleet-1",
        Audience = new RunAudience { Kind = AudienceKind.SampleCount, Count = count },
        Targets = new RunTargets(),
        WindowStart = Now.AddMinutes(-10),
        WindowEnd = Now.AddMinutes(20),
        Counters = new RunCounters { Delivered = delivered },
    };

    private void ActiveRun(NetworkDiagnosticRun run)
    {
        _runs.Setup(r => r.ActiveFleetRunsAsync(Now)).ReturnsAsync([run]);
        _runs.Setup(r => r.GetAsync(run.Id)).ReturnsAsync(run);
    }

    private void TopUpSetup(NetworkDiagnosticRun run, int pending, params string[] assigned)
    {
        ActiveRun(run);
        _assignments.Setup(a => a.CountPendingUndeliveredAsync(run.Id, It.IsAny<IReadOnlyCollection<string>>(), Now)).ReturnsAsync(pending);
        _assignments.Setup(a => a.AssignedBattleTagsAsync(run.Id)).ReturnsAsync(assigned.ToList());
    }

    [Test]
    public async Task Tick_HandsTheWholeDueBatchToTheDeliveryService_P4()
    {
        Online("A#1111", "B#2222");
        var run = CountRun(5, 0);
        var first = AssignmentFactory.Create(run.Id, "A#1111", Now.AddMinutes(-1), DeliveryRules.FleetDeliverBy(run));
        var second = AssignmentFactory.Create(run.Id, "B#2222", Now.AddMinutes(-1), DeliveryRules.FleetDeliverBy(run));
        _assignments.Setup(a => a.DueUndeliveredAsync(It.Is<IReadOnlyCollection<string>>(t => t.Count == 2), Now, 500)).ReturnsAsync([first, second]);

        await _scheduler.TickAsync(CancellationToken.None);

        // Deliverability, per-assignment failure handling and run lookup live in PushDueAsync (Task 9).
        _delivery.Verify(d => d.PushDueAsync(It.Is<IReadOnlyCollection<NetworkDiagnosticAssignment>>(l => l.Count == 2 && l.Contains(first) && l.Contains(second)), Now), Times.Once);
        _delivery.Verify(d => d.PushAsync(It.IsAny<NetworkDiagnosticAssignment>(), It.IsAny<NetworkDiagnosticRun>()), Times.Never);
    }

    [Test]
    public async Task Tick_NobodyOnline_SkipsThePushQuery()
    {
        await _scheduler.TickAsync(CancellationToken.None);

        _assignments.Verify(a => a.DueUndeliveredAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<DateTime>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task Tick_TopUp_AssignsOnlyTheShortfall_FromUnassignedOnlineUsers()
    {
        Online("A#1111", "B#2222", "C#3333", "D#4444");
        var run = CountRun(count: 3, delivered: 1);
        ActiveRun(run);
        _assignments.Setup(a => a.CountPendingUndeliveredAsync(run.Id, It.IsAny<IReadOnlyCollection<string>>(), Now)).ReturnsAsync(1);
        _assignments.Setup(a => a.AssignedBattleTagsAsync(run.Id)).ReturnsAsync(["A#1111", "B#2222"]);
        var inserted = new List<NetworkDiagnosticAssignment>();
        _assignments.Setup(a => a.TryInsertAsync(It.IsAny<NetworkDiagnosticAssignment>()))
            .Callback<NetworkDiagnosticAssignment>(inserted.Add).ReturnsAsync(true);

        await _scheduler.TickAsync(CancellationToken.None);

        Assert.AreEqual(1, inserted.Count, "3 wanted - 1 delivered - 1 pending = 1");
        Assert.AreEqual("C#3333", inserted[0].BattleTag, "QueueRandom(0) picks the first unassigned candidate");
        Assert.AreEqual(Now, inserted[0].DeliverAfter);
        Assert.AreEqual(run.WindowEnd!.Value + NetDiagConstants.FleetSubmitGrace, inserted[0].DeliverBy);
        _runs.Verify(r => r.IncrementCountersAsync(run.Id, 1, 0, 0, 0), Times.Once);
    }

    [Test]
    public async Task Tick_TopUp_NothingWhenTheCountIsCovered()
    {
        Online("A#1111");
        var run = CountRun(count: 2, delivered: 1);
        ActiveRun(run);
        _assignments.Setup(a => a.CountPendingUndeliveredAsync(run.Id, It.IsAny<IReadOnlyCollection<string>>(), Now)).ReturnsAsync(1);

        await _scheduler.TickAsync(CancellationToken.None);

        _assignments.Verify(a => a.TryInsertAsync(It.IsAny<NetworkDiagnosticAssignment>()), Times.Never);
    }

    [Test]
    public async Task Tick_TopUp_StopsAtWindowEnd_EvenInsideTheSubmitGrace()
    {
        Online("A#1111");
        _runs.Setup(r => r.ActiveFleetRunsAsync(Now)).ReturnsAsync([CountRun(5, 0) with { WindowEnd = Now }]);

        await _scheduler.TickAsync(CancellationToken.None);

        _assignments.Verify(a => a.CountPendingUndeliveredAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<DateTime>()), Times.Never);
        _assignments.Verify(a => a.TryInsertAsync(It.IsAny<NetworkDiagnosticAssignment>()), Times.Never);
    }

    [Test]
    public async Task Tick_TopUp_IgnoresNonCountFleetRuns()
    {
        Online("A#1111");
        _runs.Setup(r => r.ActiveFleetRunsAsync(Now)).ReturnsAsync([CountRun(5, 0) with { Audience = new RunAudience { Kind = AudienceKind.All } }]);

        await _scheduler.TickAsync(CancellationToken.None);

        _assignments.Verify(a => a.CountPendingUndeliveredAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Test]
    public async Task Tick_Expiry_IncrementsExpiredPerRun()
    {
        _assignments.Setup(a => a.ExpireOverdueAsync(Now)).ReturnsAsync(new Dictionary<string, int> { ["run-1"] = 3, ["run-2"] = 1 });

        await _scheduler.TickAsync(CancellationToken.None);

        _runs.Verify(r => r.IncrementCountersAsync("run-1", 0, 0, 0, 3), Times.Once);
        _runs.Verify(r => r.IncrementCountersAsync("run-2", 0, 0, 0, 1), Times.Once);
    }

    [Test]
    public async Task Tick_Expiry_AFailingIncrement_DoesNotDropTheOtherRunsCounters()
    {
        _assignments.Setup(a => a.ExpireOverdueAsync(Now)).ReturnsAsync(new Dictionary<string, int> { ["run-1"] = 3, ["run-2"] = 1 });
        _runs.Setup(r => r.IncrementCountersAsync("run-1", 0, 0, 0, 3)).ThrowsAsync(new TimeoutException());

        await _scheduler.TickAsync(CancellationToken.None);

        _runs.Verify(r => r.IncrementCountersAsync("run-2", 0, 0, 0, 1), Times.Once);
    }

    [Test]
    public async Task Tick_TopUp_SecondInsertThrows_TheFirstIsStillCounted_AndCancelChecked()
    {
        Online("A#1111", "B#2222", "C#3333");
        var run = CountRun(count: 3, delivered: 0);
        TopUpSetup(run, pending: 0);
        _assignments.SetupSequence(a => a.TryInsertAsync(It.IsAny<NetworkDiagnosticAssignment>()))
            .ReturnsAsync(true).ThrowsAsync(new TimeoutException());

        await _scheduler.TickAsync(CancellationToken.None);

        _runs.Verify(r => r.IncrementCountersAsync(run.Id, 1, 0, 0, 0), Times.Once);
        _delivery.Verify(d => d.ExpireIfCancelledAsync(run.Id), Times.Once);
        _assignments.Verify(a => a.ExpireOverdueAsync(Now), Times.Once, "the later step still runs");
    }

    [Test]
    public void Tick_TopUp_CancelledMidLoop_TheInsertedOnesAreStillCounted()
    {
        Online("A#1111", "B#2222");
        using var cts = new CancellationTokenSource();
        var run = CountRun(count: 2, delivered: 0);
        TopUpSetup(run, pending: 0);
        _assignments.Setup(a => a.TryInsertAsync(It.IsAny<NetworkDiagnosticAssignment>()))
            .Callback<NetworkDiagnosticAssignment>(_ => cts.Cancel()).ReturnsAsync(true);

        Assert.ThrowsAsync<OperationCanceledException>(() => _scheduler.TickAsync(cts.Token));

        _runs.Verify(r => r.IncrementCountersAsync(run.Id, 1, 0, 0, 0), Times.Once);
    }

    [Test]
    public async Task Tick_TopUp_RunsTheCancelRaceGuardOnlyWhenSomethingWasInserted()
    {
        Online("A#1111");
        var run = CountRun(count: 1, delivered: 0);
        TopUpSetup(run, pending: 0);
        _assignments.Setup(a => a.TryInsertAsync(It.IsAny<NetworkDiagnosticAssignment>())).ReturnsAsync(false);

        await _scheduler.TickAsync(CancellationToken.None);

        _delivery.Verify(d => d.ExpireIfCancelledAsync(It.IsAny<string>()), Times.Never);
        _runs.Verify(r => r.IncrementCountersAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never, "a lost insert race is not counted");
    }

    [Test]
    public async Task Tick_TopUp_CountsOnlyWonInserts()
    {
        Online("A#1111", "B#2222");
        var run = CountRun(count: 2, delivered: 0);
        TopUpSetup(run, pending: 0);
        _assignments.SetupSequence(a => a.TryInsertAsync(It.IsAny<NetworkDiagnosticAssignment>())).ReturnsAsync(false).ReturnsAsync(true);

        await _scheduler.TickAsync(CancellationToken.None);

        _runs.Verify(r => r.IncrementCountersAsync(run.Id, 1, 0, 0, 0), Times.Once);
        _delivery.Verify(d => d.ExpireIfCancelledAsync(run.Id), Times.Once);
    }

    [Test]
    public async Task Tick_TopUp_FewerCandidatesThanNeed_AssignsWhatItHas()
    {
        Online("A#1111", "B#2222");
        var run = CountRun(count: 5, delivered: 0);
        TopUpSetup(run, pending: 0, "A#1111");
        _assignments.Setup(a => a.TryInsertAsync(It.IsAny<NetworkDiagnosticAssignment>())).ReturnsAsync(true);

        await _scheduler.TickAsync(CancellationToken.None);

        _assignments.Verify(a => a.TryInsertAsync(It.Is<NetworkDiagnosticAssignment>(x => x.BattleTag == "B#2222")), Times.Once);
        _assignments.Verify(a => a.TryInsertAsync(It.IsAny<NetworkDiagnosticAssignment>()), Times.Once);
        _runs.Verify(r => r.IncrementCountersAsync(run.Id, 1, 0, 0, 0), Times.Once);
    }

    [Test]
    public async Task Tick_TopUp_CancelledRun_IsSkipped()
    {
        Online("A#1111");
        _runs.Setup(r => r.ActiveFleetRunsAsync(Now)).ReturnsAsync([CountRun(5, 0) with { CancelledAt = Now.AddSeconds(-1) }]);

        await _scheduler.TickAsync(CancellationToken.None);

        _assignments.Verify(a => a.CountPendingUndeliveredAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Test]
    public async Task Tick_TopUp_WindowNotOpenYet_IsSkipped()
    {
        Online("A#1111");
        _runs.Setup(r => r.ActiveFleetRunsAsync(Now)).ReturnsAsync([CountRun(5, 0) with { WindowStart = Now.AddSeconds(1) }]);

        await _scheduler.TickAsync(CancellationToken.None);

        _assignments.Verify(a => a.CountPendingUndeliveredAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Test]
    public async Task Tick_TopUp_UsesTheDeliveredCountFromAFreshRunRead()
    {
        Online("A#1111");
        var stale = CountRun(count: 1, delivered: 0);
        TopUpSetup(stale, pending: 0);
        _runs.Setup(r => r.GetAsync(stale.Id)).ReturnsAsync(CountRun(count: 1, delivered: 1));

        await _scheduler.TickAsync(CancellationToken.None);

        _assignments.Verify(a => a.TryInsertAsync(It.IsAny<NetworkDiagnosticAssignment>()), Times.Never);
    }

    [Test]
    public async Task Tick_TopUp_RunCancelledBetweenListAndRead_IsSkipped()
    {
        Online("A#1111");
        var listed = CountRun(count: 1, delivered: 0);
        TopUpSetup(listed, pending: 0);
        _runs.Setup(r => r.GetAsync(listed.Id)).ReturnsAsync(listed with { CancelledAt = Now });

        await _scheduler.TickAsync(CancellationToken.None);

        _assignments.Verify(a => a.TryInsertAsync(It.IsAny<NetworkDiagnosticAssignment>()), Times.Never);
    }

    [Test]
    public async Task Tick_PushStepThrows_TopUpAndExpireStillRun()
    {
        Online("A#1111");
        _delivery.Setup(d => d.PushDueAsync(It.IsAny<IReadOnlyCollection<NetworkDiagnosticAssignment>>(), Now)).ThrowsAsync(new TimeoutException());
        _assignments.Setup(a => a.DueUndeliveredAsync(It.IsAny<IReadOnlyCollection<string>>(), Now, 500))
            .ReturnsAsync([AssignmentFactory.Create("run-1", "A#1111", Now, Now.AddHours(1))]);

        await _scheduler.TickAsync(CancellationToken.None);

        _runs.Verify(r => r.ActiveFleetRunsAsync(Now), Times.Once);
        _assignments.Verify(a => a.ExpireOverdueAsync(Now), Times.Once);
    }

    [Test]
    public void Tick_Cancelled_RunsNoStepAndPropagates()
    {
        Online("A#1111");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(() => _scheduler.TickAsync(cts.Token));

        _assignments.Verify(a => a.ExpireOverdueAsync(It.IsAny<DateTime>()), Times.Never);
        _assignments.Verify(a => a.DueUndeliveredAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<DateTime>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public void Tick_CancelledDuringAStep_SkipsTheRemainingSteps()
    {
        Online("A#1111");
        using var cts = new CancellationTokenSource();
        _assignments.Setup(a => a.DueUndeliveredAsync(It.IsAny<IReadOnlyCollection<string>>(), Now, 500))
            .Returns(() => { cts.Cancel(); throw new OperationCanceledException(cts.Token); });

        Assert.ThrowsAsync<OperationCanceledException>(() => _scheduler.TickAsync(cts.Token));

        _runs.Verify(r => r.ActiveFleetRunsAsync(It.IsAny<DateTime>()), Times.Never);
        _assignments.Verify(a => a.ExpireOverdueAsync(It.IsAny<DateTime>()), Times.Never);
    }

    [Test]
    public async Task Tick_AFailingStep_DoesNotStopTheOthers()
    {
        Online("A#1111");
        _assignments.Setup(a => a.DueUndeliveredAsync(It.IsAny<IReadOnlyCollection<string>>(), Now, 500)).ThrowsAsync(new TimeoutException());
        _runs.Setup(r => r.ActiveFleetRunsAsync(Now)).ThrowsAsync(new TimeoutException());

        await _scheduler.TickAsync(CancellationToken.None);

        _assignments.Verify(a => a.ExpireOverdueAsync(Now), Times.Once);
    }

    [Test]
    public void AddNetworkDiagnostics_ResolvesThePlainScheduler_AndRegistersTheDispatcher()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignalR();
        services.AddSingleton<ConnectionMapping>();
        services.AddNetworkDiagnostics();
        services.AddSingleton(_runs.Object);
        services.AddSingleton(_assignments.Object);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var scheduler = provider.GetRequiredService<INetworkDiagnosticsScheduler>();

        Assert.AreEqual(typeof(NetworkDiagnosticsScheduler), scheduler.GetType());
        Assert.IsTrue(provider.GetServices<IHostedService>().Any(s => s is NetworkDiagnosticsDispatcher));
    }
}
