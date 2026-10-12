using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
public class NetworkDiagnosticsDeliveryTests
{
    private static readonly DateTime Now = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

    private ConnectionMapping _connections;
    private Mock<IClientProxy> _proxy;
    private List<IReadOnlyList<string>> _targets;
    private Mock<INetworkDiagnosticsRunRepository> _runs;
    private Mock<INetworkDiagnosticsAssignmentRepository> _assignments;
    private IHubContext<WebsiteBackendHub> _hub;
    private NetworkDiagnosticsDelivery _delivery;

    [SetUp]
    public void SetUp()
    {
        _connections = new ConnectionMapping();
        _proxy = new Mock<IClientProxy>();
        _targets = [];
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Clients(It.IsAny<IReadOnlyList<string>>()))
            .Callback<IReadOnlyList<string>>(ids => _targets.Add(ids))
            .Returns(_proxy.Object);
        var hub = new Mock<IHubContext<WebsiteBackendHub>>();
        hub.Setup(h => h.Clients).Returns(clients.Object);
        _runs = new Mock<INetworkDiagnosticsRunRepository>();
        _assignments = new Mock<INetworkDiagnosticsAssignmentRepository>();
        _runs.Setup(r => r.ActiveFleetRunsAsync(It.IsAny<DateTime>())).ReturnsAsync([]);
        _assignments.Setup(a => a.DueForAsync(It.IsAny<string>(), It.IsAny<DateTime>())).ReturnsAsync([]);
        _hub = hub.Object;
        _delivery = Delivery(NullLogger<NetworkDiagnosticsDelivery>.Instance);
    }

    private NetworkDiagnosticsDelivery Delivery(ILogger<NetworkDiagnosticsDelivery> log) => new(
        _hub, _connections, _runs.Object, _assignments.Object, new FixedTimeProvider(new DateTimeOffset(Now)), new QueueRandom(0.5), log);

    private static NetworkDiagnosticRun UsersRun() => new()
    {
        Id = "run-1",
        Audience = new RunAudience { Kind = AudienceKind.Users, BattleTags = ["Name#1234"] },
        Targets = new RunTargets { FloNodeIds = [12], CoreServers = ["bigboi"], IncludeDefaultGateway = true },
        ResolvedEndpoints = [new RunEndpoint { TargetKey = "core:bigboi", Label = "bigboi", Ip = "157.90.1.251", TcpPort = 443 }],
        Methods = ["IcmpMtr", "TcpPing"],
        WindowSeconds = 60,
    };

    private static NetworkDiagnosticRun FleetRun(DateTime windowEnd) => UsersRun() with
    {
        Id = "fleet-1",
        Audience = new RunAudience { Kind = AudienceKind.All },
        WindowStart = windowEnd.AddMinutes(-30),
        WindowEnd = windowEnd,
    };

    private static NetworkDiagnosticAssignment Assignment(string runId = "run-1") =>
        AssignmentFactory.Create(runId, "Name#1234", Now.AddMinutes(-1), Now.AddDays(14));

    // SendAsync(method, arg) is an extension that calls SendCoreAsync(method, new[] { arg }, ct).
    private NetworkDiagnosticsRequestDto SentPayload(int call = 0) =>
        (NetworkDiagnosticsRequestDto)((object[])_proxy.Invocations[call].Arguments[1])[0];

    [Test]
    public async Task Push_SendsToEveryConnection_AndCountsDeliveredOnce()
    {
        _connections.Add("c1", new WebSocketUser { BattleTag = "Name#1234", ConnectionId = "c1" });
        _connections.Add("c2", new WebSocketUser { BattleTag = "Name#1234", ConnectionId = "c2" });
        var a = Assignment();
        _assignments.SetupSequence(x => x.TryMarkDeliveredAsync(a.Id, Now)).ReturnsAsync(true).ReturnsAsync(false);

        Assert.IsTrue(await _delivery.PushAsync(a, UsersRun()));
        Assert.IsTrue(await _delivery.PushAsync(a, UsersRun()));

        Assert.That(_targets[0], Is.EquivalentTo(new[] { "c1", "c2" }));
        _proxy.Verify(p => p.SendCoreAsync(NetDiagConstants.HubEventName, It.IsAny<object[]>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _runs.Verify(r => r.IncrementCountersAsync("run-1", 0, 1, 0, 0), Times.Once);
        Assert.AreEqual(SentPayload(0).Token, SentPayload(1).Token, "a re-push carries the same token");
    }

    [Test]
    public async Task Push_PayloadMatchesC4()
    {
        _connections.Add("c1", new WebSocketUser { BattleTag = "Name#1234", ConnectionId = "c1" });
        var a = Assignment();

        await _delivery.PushAsync(a, UsersRun());

        var dto = SentPayload();
        Assert.AreEqual(a.Id, dto.MeasurementId);
        Assert.AreEqual(a.Token, dto.Token, "A10: the stored token is sent");
        Assert.AreEqual(43, dto.Token.Length);
        Assert.AreEqual(a.DeliverBy, dto.DeliverBy);
        Assert.AreEqual(new[] { "IcmpMtr", "TcpPing" }, dto.Spec.Methods);
        Assert.AreEqual(60, dto.Spec.WindowSeconds);
        Assert.AreEqual(new[] { 12 }, dto.Spec.FloNodeIds);
        Assert.IsFalse(dto.Spec.AllFloNodes);
        Assert.IsTrue(dto.Spec.IncludeDefaultGateway);
        Assert.AreEqual("core:bigboi", dto.Spec.Endpoints[0].TargetKey);
    }

    [Test]
    public async Task Push_Offline_SendsNothing()
    {
        Assert.IsFalse(await _delivery.PushAsync(Assignment(), UsersRun()));
        _assignments.Verify(x => x.TryMarkDeliveredAsync(It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Test]
    public async Task OnConnected_PushesDueAssignments()
    {
        _connections.Add("c1", new WebSocketUser { BattleTag = "Name#1234", ConnectionId = "c1" });
        var a = Assignment();
        _assignments.Setup(x => x.DueForAsync("Name#1234", Now)).ReturnsAsync([a]);
        _runs.Setup(r => r.GetAsync("run-1")).ReturnsAsync(UsersRun());

        await _delivery.OnUserConnectedAsync("Name#1234");

        Assert.AreEqual(a.Id, SentPayload().MeasurementId);
    }

    [Test]
    public async Task OnConnected_LateJoiner_GetsASlotInTheRemainingWindow()
    {
        var run = FleetRun(Now.AddMinutes(10));
        _runs.Setup(r => r.ActiveFleetRunsAsync(Now)).ReturnsAsync([run]);
        _assignments.Setup(x => x.ExistsAsync("fleet-1", "Name#1234")).ReturnsAsync(false);
        NetworkDiagnosticAssignment inserted = null;
        _assignments.Setup(x => x.TryInsertAsync(It.IsAny<NetworkDiagnosticAssignment>()))
            .Callback<NetworkDiagnosticAssignment>(x => inserted = x).ReturnsAsync(true);

        await _delivery.OnUserConnectedAsync("Name#1234");

        Assert.AreEqual(Now.AddMinutes(5), inserted.DeliverAfter, "QueueRandom(0.5) puts the slot mid-way");
        Assert.AreEqual(Now.AddMinutes(10) + NetDiagConstants.FleetSubmitGrace, inserted.DeliverBy);
        _runs.Verify(r => r.IncrementCountersAsync("fleet-1", 1, 0, 0, 0), Times.Once);
    }

    [Test]
    public async Task OnConnected_LateJoiner_AlreadyAssignedOrRaceLost_NotCounted()
    {
        var run = FleetRun(Now.AddMinutes(10));
        _runs.Setup(r => r.ActiveFleetRunsAsync(Now)).ReturnsAsync([run, run with { Id = "fleet-2" }]);
        _assignments.Setup(x => x.ExistsAsync("fleet-1", "Name#1234")).ReturnsAsync(true);
        _assignments.Setup(x => x.ExistsAsync("fleet-2", "Name#1234")).ReturnsAsync(false);
        _assignments.Setup(x => x.TryInsertAsync(It.IsAny<NetworkDiagnosticAssignment>())).ReturnsAsync(false);

        await _delivery.OnUserConnectedAsync("Name#1234");

        _assignments.Verify(x => x.TryInsertAsync(It.Is<NetworkDiagnosticAssignment>(a => a.RunId == "fleet-1")), Times.Never);
        _runs.Verify(r => r.IncrementCountersAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    // A10: the token is never logged (late-joiner assignment logs the BattleTag, run and slot only).
    [Test]
    public async Task LateJoiner_LogsNeverContainTheToken()
    {
        var log = new ListLogger<NetworkDiagnosticsDelivery>();
        var delivery = Delivery(log);
        _runs.Setup(r => r.ActiveFleetRunsAsync(Now)).ReturnsAsync([FleetRun(Now.AddMinutes(10))]);
        _assignments.Setup(x => x.ExistsAsync("fleet-1", "Name#1234")).ReturnsAsync(false);
        NetworkDiagnosticAssignment inserted = null;
        _assignments.Setup(x => x.TryInsertAsync(It.IsAny<NetworkDiagnosticAssignment>()))
            .Callback<NetworkDiagnosticAssignment>(x => inserted = x).ReturnsAsync(true);

        await delivery.OnUserConnectedAsync("Name#1234");

        StringAssert.Contains("fleet-1", log.AllText);
        StringAssert.DoesNotContain(inserted.Token, log.AllText);
    }

    [Test]
    public async Task OnConnected_FleetGracePeriod_RePushesTheDeliveredAssignment_WithTheSameToken()
    {
        // A6: WindowEnd passed 5 min ago, DeliverBy = WindowEnd + 15 min is still ahead.
        _connections.Add("c1", new WebSocketUser { BattleTag = "Name#1234", ConnectionId = "c1" });
        var run = FleetRun(Now.AddMinutes(-5));
        var a = AssignmentFactory.Create(run.Id, "Name#1234", Now.AddMinutes(-20), DeliveryRules.FleetDeliverBy(run))
            with
        { DeliveredAt = Now.AddMinutes(-20) };
        _assignments.Setup(x => x.DueForAsync("Name#1234", Now)).ReturnsAsync([a]);
        _runs.Setup(r => r.GetAsync(run.Id)).ReturnsAsync(run);

        await _delivery.OnUserConnectedAsync("Name#1234");

        Assert.AreEqual(a.Token, SentPayload().Token);
        Assert.AreEqual(Now.AddMinutes(10), SentPayload().DeliverBy);
    }

    [Test]
    public async Task OnConnected_FleetGracePeriod_NoFirstDelivery_AndNoLateJoiner()
    {
        // A6: after WindowEnd, an undelivered assignment is not pushed and no new assignment is made.
        _connections.Add("c1", new WebSocketUser { BattleTag = "Name#1234", ConnectionId = "c1" });
        var run = FleetRun(Now.AddMinutes(-5));
        var a = AssignmentFactory.Create(run.Id, "Name#1234", Now.AddMinutes(-20), DeliveryRules.FleetDeliverBy(run));
        _runs.Setup(r => r.ActiveFleetRunsAsync(Now)).ReturnsAsync([run with { Id = "fleet-2" }]); // defensive: even if returned
        _assignments.Setup(x => x.ExistsAsync("fleet-2", "Name#1234")).ReturnsAsync(false);
        _assignments.Setup(x => x.DueForAsync("Name#1234", Now)).ReturnsAsync([a]);
        _runs.Setup(r => r.GetAsync(run.Id)).ReturnsAsync(run);

        await _delivery.OnUserConnectedAsync("Name#1234");

        Assert.IsEmpty(_proxy.Invocations);
        _assignments.Verify(x => x.TryInsertAsync(It.IsAny<NetworkDiagnosticAssignment>()), Times.Never);
    }

    [TestCase(-1, false, false)]   // window closed, never delivered: no first delivery in the grace period
    [TestCase(-1, true, true)]     // window closed, delivered before: re-push allowed (same token)
    [TestCase(5, false, true)]     // window open
    public void IsDeliverable_FleetWindowRule(int windowEndMinutes, bool delivered, bool expected)
    {
        var a = Assignment("fleet-1") with { DeliveredAt = delivered ? Now.AddMinutes(-20) : null };
        Assert.AreEqual(expected, DeliveryRules.IsDeliverable(a, FleetRun(Now.AddMinutes(windowEndMinutes)), Now));
    }

    [Test]
    public void IsDeliverable_CancelledOrNotPendingOrOutsideBounds_False()
    {
        Assert.IsFalse(DeliveryRules.IsDeliverable(Assignment(), UsersRun() with { CancelledAt = Now }, Now));
        Assert.IsFalse(DeliveryRules.IsDeliverable(Assignment() with { Status = AssignmentStatus.Submitted }, UsersRun(), Now));
        Assert.IsFalse(DeliveryRules.IsDeliverable(Assignment() with { DeliverAfter = Now.AddSeconds(1) }, UsersRun(), Now));
        Assert.IsFalse(DeliveryRules.IsDeliverable(Assignment() with { DeliverBy = Now }, UsersRun(), Now));
        Assert.IsTrue(DeliveryRules.IsDeliverable(Assignment(), UsersRun(), Now));
    }

    // Ruling P4: one failing push must not starve the assignments after it.
    [Test]
    public async Task PushDue_FirstPushThrows_SecondIsStillPushed_AndTheFailureIsLoggedWithoutTheToken()
    {
        var log = new ListLogger<NetworkDiagnosticsDelivery>();
        var delivery = Delivery(log);
        _connections.Add("c1", new WebSocketUser { BattleTag = "Name#1234", ConnectionId = "c1" });
        var first = Assignment();
        var second = Assignment("run-2");
        _runs.Setup(r => r.GetAsync("run-1")).ReturnsAsync(UsersRun());
        _runs.Setup(r => r.GetAsync("run-2")).ReturnsAsync(UsersRun() with { Id = "run-2" });
        _proxy.SetupSequence(p => p.SendCoreAsync(NetDiagConstants.HubEventName, It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("send failed"))
            .Returns(Task.CompletedTask);
        _assignments.Setup(x => x.TryMarkDeliveredAsync(second.Id, Now)).ReturnsAsync(true);

        var pushed = await delivery.PushDueAsync([first, second], Now);

        Assert.AreEqual(1, pushed);
        Assert.AreEqual(second.Id, SentPayload(1).MeasurementId);
        _assignments.Verify(x => x.TryMarkDeliveredAsync(first.Id, It.IsAny<DateTime>()), Times.Never);
        _assignments.Verify(x => x.TryMarkDeliveredAsync(second.Id, Now), Times.Once);
        _runs.Verify(r => r.IncrementCountersAsync("run-2", 0, 1, 0, 0), Times.Once);
        StringAssert.Contains(first.Id, log.AllText);
        StringAssert.Contains("run-1", log.AllText);
        StringAssert.Contains("Name#1234", log.AllText);
        StringAssert.DoesNotContain(first.Token, log.AllText);
        StringAssert.DoesNotContain(second.Token, log.AllText);
    }

    [Test]
    public async Task PushDue_RunLookupThrowsForOneAssignment_TheOthersAreStillPushed()
    {
        _connections.Add("c1", new WebSocketUser { BattleTag = "Name#1234", ConnectionId = "c1" });
        var first = Assignment();
        var second = Assignment("run-2");
        _runs.Setup(r => r.GetAsync("run-1")).ThrowsAsync(new TimeoutException("mongo down"));
        _runs.Setup(r => r.GetAsync("run-2")).ReturnsAsync(UsersRun() with { Id = "run-2" });

        var pushed = await _delivery.PushDueAsync([first, second], Now);

        Assert.AreEqual(1, pushed);
        Assert.AreEqual(second.Id, SentPayload().MeasurementId);
    }

    [Test]
    public async Task PushDue_LooksEachRunUpOnce_AndSkipsUndeliverableMissingRunAndOfflineUsers()
    {
        _connections.Add("c1", new WebSocketUser { BattleTag = "Name#1234", ConnectionId = "c1" });
        var a = Assignment();
        var b = Assignment();
        var notYetDue = Assignment() with { DeliverAfter = Now.AddMinutes(1) };
        var runGone = Assignment("gone");
        var offline = AssignmentFactory.Create("run-1", "Other#5678", Now.AddMinutes(-1), Now.AddDays(14));
        _runs.Setup(r => r.GetAsync("run-1")).ReturnsAsync(UsersRun());
        _runs.Setup(r => r.GetAsync("gone")).ReturnsAsync((NetworkDiagnosticRun)null);

        var pushed = await _delivery.PushDueAsync([a, b, notYetDue, runGone, offline], Now);

        Assert.AreEqual(2, pushed, "an offline user and an undeliverable assignment do not count");
        Assert.AreEqual(2, _proxy.Invocations.Count);
        _runs.Verify(r => r.GetAsync("run-1"), Times.Once);
        _runs.Verify(r => r.GetAsync("gone"), Times.Once);
    }

    [Test]
    public async Task PushDue_CancelledRun_PushesNothing()
    {
        _connections.Add("c1", new WebSocketUser { BattleTag = "Name#1234", ConnectionId = "c1" });
        _runs.Setup(r => r.GetAsync("run-1")).ReturnsAsync(UsersRun() with { CancelledAt = Now.AddMinutes(-1) });

        Assert.AreEqual(0, await _delivery.PushDueAsync([Assignment()], Now));

        Assert.IsEmpty(_proxy.Invocations);
    }

    // Cancel race (Task 6 review): a late-joiner insert that loses against a concurrent cancel must not stay Pending.
    [Test]
    public async Task OnConnected_LateJoiner_RunCancelledWhileInserting_TheFreshAssignmentIsExpired()
    {
        var log = new ListLogger<NetworkDiagnosticsDelivery>();
        var delivery = Delivery(log);
        var run = FleetRun(Now.AddMinutes(10));
        _runs.Setup(r => r.ActiveFleetRunsAsync(Now)).ReturnsAsync([run]);
        _runs.Setup(r => r.GetAsync("fleet-1")).ReturnsAsync(run with { CancelledAt = Now });
        _assignments.Setup(x => x.ExistsAsync("fleet-1", "Name#1234")).ReturnsAsync(false);
        NetworkDiagnosticAssignment inserted = null;
        _assignments.Setup(x => x.TryInsertAsync(It.IsAny<NetworkDiagnosticAssignment>()))
            .Callback<NetworkDiagnosticAssignment>(x => inserted = x).ReturnsAsync(true);
        _assignments.Setup(x => x.ExpireAllPendingAsync("fleet-1")).ReturnsAsync(1);

        await delivery.OnUserConnectedAsync("Name#1234");

        _assignments.Verify(x => x.ExpireAllPendingAsync("fleet-1"), Times.Once);
        _runs.Verify(r => r.IncrementCountersAsync("fleet-1", 1, 0, 0, 0), Times.Once);
        _runs.Verify(r => r.IncrementCountersAsync("fleet-1", 0, 0, 0, 1), Times.Once);
        StringAssert.DoesNotContain(inserted.Token, log.AllText);
    }

    [Test]
    public async Task OnConnected_LateJoiner_CancelAlreadyExpiredTheFreshAssignment_ExpiredIsNotCountedTwice()
    {
        var run = FleetRun(Now.AddMinutes(10));
        _runs.Setup(r => r.ActiveFleetRunsAsync(Now)).ReturnsAsync([run]);
        _runs.Setup(r => r.GetAsync("fleet-1")).ReturnsAsync(run with { CancelledAt = Now });
        _assignments.Setup(x => x.ExistsAsync("fleet-1", "Name#1234")).ReturnsAsync(false);
        _assignments.Setup(x => x.TryInsertAsync(It.IsAny<NetworkDiagnosticAssignment>())).ReturnsAsync(true);
        _assignments.Setup(x => x.ExpireAllPendingAsync("fleet-1")).ReturnsAsync(0);

        await _delivery.OnUserConnectedAsync("Name#1234");

        _runs.Verify(r => r.IncrementCountersAsync("fleet-1", 0, 0, 0, It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task OnConnected_LateJoiner_RunStillActiveAfterInsert_NothingIsExpired()
    {
        var run = FleetRun(Now.AddMinutes(10));
        _runs.Setup(r => r.ActiveFleetRunsAsync(Now)).ReturnsAsync([run]);
        _runs.Setup(r => r.GetAsync("fleet-1")).ReturnsAsync(run);
        _assignments.Setup(x => x.ExistsAsync("fleet-1", "Name#1234")).ReturnsAsync(false);
        _assignments.Setup(x => x.TryInsertAsync(It.IsAny<NetworkDiagnosticAssignment>())).ReturnsAsync(true);

        await _delivery.OnUserConnectedAsync("Name#1234");

        _assignments.Verify(x => x.ExpireAllPendingAsync(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task OnConnected_LateJoiner_CancelledRun_GetsNoAssignment()
    {
        // Defensive: ActiveFleetRunsAsync filters cancelled runs, but a stale snapshot must not assign either.
        _runs.Setup(r => r.ActiveFleetRunsAsync(Now)).ReturnsAsync([FleetRun(Now.AddMinutes(10)) with { CancelledAt = Now.AddMinutes(-1) }]);
        _assignments.Setup(x => x.ExistsAsync("fleet-1", "Name#1234")).ReturnsAsync(false);

        await _delivery.OnUserConnectedAsync("Name#1234");

        _assignments.Verify(x => x.TryInsertAsync(It.IsAny<NetworkDiagnosticAssignment>()), Times.Never);
    }

    [Test]
    public async Task OnConnected_LateJoinerFailureForOneRun_StillPushesDueAssignments()
    {
        _connections.Add("c1", new WebSocketUser { BattleTag = "Name#1234", ConnectionId = "c1" });
        _runs.Setup(r => r.ActiveFleetRunsAsync(Now)).ReturnsAsync([FleetRun(Now.AddMinutes(10))]);
        _assignments.Setup(x => x.ExistsAsync("fleet-1", "Name#1234")).ThrowsAsync(new TimeoutException("mongo hiccup"));
        var a = Assignment();
        _assignments.Setup(x => x.DueForAsync("Name#1234", Now)).ReturnsAsync([a]);
        _runs.Setup(r => r.GetAsync("run-1")).ReturnsAsync(UsersRun());

        await _delivery.OnUserConnectedAsync("Name#1234");

        Assert.AreEqual(a.Id, SentPayload().MeasurementId);
    }

    // Ruling P13 / A10: a positional record's generated ToString would print every member, the token included.
    [Test]
    public void RequestDto_ToString_NeverContainsTheToken()
    {
        var a = Assignment();
        var dto = new NetworkDiagnosticsRequestDto(a.Id, a.Token, a.DeliverBy, null);

        var text = dto.ToString();

        StringAssert.Contains(a.Id, text);
        StringAssert.DoesNotContain(a.Token, text);
        StringAssert.DoesNotContain(a.Token, $"{dto}");
    }

    // C4: SignalR serializes with System.Text.Json web defaults (camelCase); the token must still be on the wire.
    [Test]
    public async Task Push_PayloadSerializesAsCamelCaseC4_WithTheToken()
    {
        _connections.Add("c1", new WebSocketUser { BattleTag = "Name#1234", ConnectionId = "c1" });
        var a = Assignment();
        await _delivery.PushAsync(a, UsersRun());

        var json = System.Text.Json.JsonSerializer.Serialize(SentPayload(), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.AreEqual(a.Id, root.GetProperty("measurementId").GetString());
        Assert.AreEqual(a.Token, root.GetProperty("token").GetString());
        Assert.AreEqual(a.DeliverBy, root.GetProperty("deliverBy").GetDateTime());
        var spec = root.GetProperty("spec");
        Assert.AreEqual("IcmpMtr", spec.GetProperty("methods")[0].GetString());
        Assert.IsFalse(spec.GetProperty("allowDuringGame").GetBoolean());
        Assert.AreEqual(60, spec.GetProperty("windowSeconds").GetInt32());
        Assert.AreEqual(12, spec.GetProperty("floNodeIds")[0].GetInt32());
        Assert.IsFalse(spec.GetProperty("allFloNodes").GetBoolean());
        Assert.IsTrue(spec.GetProperty("includeDefaultGateway").GetBoolean());
        var endpoint = spec.GetProperty("endpoints")[0];
        Assert.AreEqual("core:bigboi", endpoint.GetProperty("targetKey").GetString());
        Assert.AreEqual("157.90.1.251", endpoint.GetProperty("ip").GetString());
        Assert.AreEqual(443, endpoint.GetProperty("tcpPort").GetInt32());
    }
}

[TestFixture]
public class NetworkDiagnosticsDeliveryRegistrationTests
{
    // The hub takes INetworkDiagnosticsDelivery in its constructor: if the container cannot build it, no launcher
    // can connect. It must also be the plain class, not a tracing proxy, because it handles tokens (A10).
    [Test]
    public void AddNetworkDiagnostics_ResolvesThePlainDeliveryService_AsASingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignalR();
        services.AddSingleton<ConnectionMapping>();
        services.AddNetworkDiagnostics();
        services.AddSingleton(new Mock<INetworkDiagnosticsRunRepository>().Object);
        services.AddSingleton(new Mock<INetworkDiagnosticsAssignmentRepository>().Object);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var delivery = provider.GetRequiredService<INetworkDiagnosticsDelivery>();

        Assert.AreEqual(typeof(NetworkDiagnosticsDelivery), delivery.GetType());
        Assert.AreSame(delivery, provider.GetRequiredService<INetworkDiagnosticsDelivery>());
        Assert.IsInstanceOf<SystemRandom>(provider.GetRequiredService<IRandom>());
    }
}
