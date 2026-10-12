using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Hubs;
using W3ChampionsStatisticService.NetworkDiagnostics;
using W3ChampionsStatisticService.NetworkDiagnostics.Api;
using W3ChampionsStatisticService.NetworkDiagnostics.Delivery;
using W3ChampionsStatisticService.NetworkDiagnostics.Persistence;
using W3ChampionsStatisticService.NetworkDiagnostics.Results;
using W3ChampionsStatisticService.NetworkDiagnostics.Runs;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Ports;

namespace WC3ChampionsStatisticService.Tests.NetworkDiagnostics;

[TestFixture]
public class NetworkDiagnosticsRunServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

    private ConnectionMapping _connections;
    private Mock<INetworkDiagnosticsRunRepository> _runs;
    private Mock<INetworkDiagnosticsAssignmentRepository> _assignments;
    private Mock<INetworkDiagnosticsResultRepository> _results;
    private Mock<INetworkDiagnosticsDelivery> _delivery;
    private Mock<IFloNodeSource> _floNodes;
    private Mock<IPlayerRepository> _players;
    private Mock<IPersonalSettingsRepository> _settings;
    private List<NetworkDiagnosticAssignment> _inserted;
    private NetworkDiagnosticsRunService _service;

    [SetUp]
    public void SetUp()
    {
        _connections = new ConnectionMapping();
        _runs = new Mock<INetworkDiagnosticsRunRepository>();
        _assignments = new Mock<INetworkDiagnosticsAssignmentRepository>();
        _results = new Mock<INetworkDiagnosticsResultRepository>();
        _delivery = new Mock<INetworkDiagnosticsDelivery>();
        _floNodes = new Mock<IFloNodeSource>();
        _players = new Mock<IPlayerRepository>();
        _settings = new Mock<IPersonalSettingsRepository>();
        _inserted = [];
        _assignments.Setup(a => a.InsertManyAsync(It.IsAny<IReadOnlyCollection<NetworkDiagnosticAssignment>>()))
            .Callback<IReadOnlyCollection<NetworkDiagnosticAssignment>>(xs => _inserted.AddRange(xs)).Returns(Task.CompletedTask);
        _floNodes.Setup(f => f.GetFloNodesAsync()).ReturnsAsync(
        [
            new FloNodeInfo { Id = 12, Name = "US East 4", Location = "Ashburn", CountryId = "US" },
            new FloNodeInfo { Id = 14, Name = "EU 2", CountryId = "DE", Settings = new FloNodeInfoSettings { IsDisabled = true } },
        ]);
        _players.Setup(p => p.GetPlayerBattleTagsAsync(It.IsAny<ICollection<string>>())).ReturnsAsync(new Dictionary<string, PlayerOverallStats>());
        _settings.Setup(s => s.LoadMany(It.IsAny<string[]>())).ReturnsAsync([]);
        var config = new NetDiagTargetsConfig(
            [new CoreServerConfig { Key = "bigboi", Label = "bigboi", Ip = "203.0.113.10", TcpPort = 443 }],
            NetDiagTargetsConfig.DefaultBaselines, "/geoip/c.mmdb", "/geoip/a.mmdb");
        _config = config;
        _service = ServiceWith(config);
    }

    private NetDiagTargetsConfig _config;

    private NetworkDiagnosticsRunService ServiceWith(
        NetDiagTargetsConfig config, INetworkDiagnosticsDelivery delivery = null, ILogger<NetworkDiagnosticsRunService> log = null) =>
        new(_runs.Object, _assignments.Object, _results.Object, delivery ?? _delivery.Object, _floNodes.Object,
            _players.Object, _settings.Object, config, _connections,
            new FixedTimeProvider(new DateTimeOffset(Now)), new QueueRandom(0.0), log ?? NullLogger<NetworkDiagnosticsRunService>.Instance);

    /// <summary>The real delivery service over this fixture's repository doubles: cancel and delivery share one expire-and-count helper.</summary>
    private NetworkDiagnosticsDelivery RealDelivery() =>
        new(Mock.Of<IHubContext<WebsiteBackendHub>>(), _connections, _runs.Object, _assignments.Object,
            new FixedTimeProvider(new DateTimeOffset(Now)), new QueueRandom(0.0), NullLogger<NetworkDiagnosticsDelivery>.Instance);

    private void KnownPlayers(params string[] tags) =>
        _players.Setup(p => p.GetPlayerBattleTagsAsync(It.IsAny<ICollection<string>>()))
            .ReturnsAsync(tags.ToDictionary(t => t, t => PlayerOverallStats.Create(t)));

    private void Online(params string[] tags)
    {
        foreach (var tag in tags) _connections.Add("conn-" + tag, new WebSocketUser { BattleTag = tag, ConnectionId = "conn-" + tag });
    }

    private static CreateRunRequest Request(RunAudience audience, RunTargets targets = null) => new()
    {
        Audience = audience,
        Targets = targets ?? new RunTargets { CoreServers = ["bigboi"], Baselines = ["cloudflare"] },
        Methods = ["IcmpMtr", "TcpPing"],
    };

    // Relaxed escaping so ">" in a message reads as itself; over HTTP it is the equivalent "\u003E".
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string ErrorOf(NetDiagResult r) => JsonSerializer.Serialize(r.Body, Json);

    [Test]
    public async Task Create_Users_AssignsEveryTagFor14Days_StoresTheRun_AndPushesOnlineUsers()
    {
        KnownPlayers("Name#1234", "Other#5678");
        Online("Name#1234");
        NetworkDiagnosticRun stored = null;
        _runs.Setup(r => r.InsertAsync(It.IsAny<NetworkDiagnosticRun>())).Callback<NetworkDiagnosticRun>(r => stored = r).Returns(Task.CompletedTask);

        var result = await _service.CreateRunAsync(Request(new RunAudience { Kind = AudienceKind.Users, BattleTags = ["Name#1234", "Other#5678"] }), "Admin#1");

        Assert.AreEqual(201, result.StatusCode);
        var dto = (RunDto)result.Body;
        Assert.AreEqual(75, dto.ExpectedDurationSeconds);
        Assert.AreEqual(2, dto.Counters.Assigned);
        Assert.AreEqual("Admin#1", stored.CreatedBy);
        Assert.AreEqual(Now.AddDays(14 + 365), stored.ExpiresAt);
        Assert.IsNull(stored.WindowStart);
        Assert.AreEqual(new[] { "core:bigboi", "baseline:cloudflare" }, stored.ResolvedEndpoints.Select(e => e.TargetKey));
        Assert.That(_inserted.Select(a => a.BattleTag), Is.EquivalentTo(new[] { "Name#1234", "Other#5678" }));
        Assert.IsTrue(_inserted.All(a => a.DeliverAfter == Now && a.DeliverBy == Now.AddDays(14) && a.RunId == stored.Id));
        // P2: only the online user is pushed, through the delivery service's one push loop; the offline one waits for connect.
        _delivery.Verify(d => d.PushDueAsync(
            It.Is<IReadOnlyCollection<NetworkDiagnosticAssignment>>(xs => xs.Single().BattleTag == "Name#1234" && xs.Single().RunId == stored.Id), Now), Times.Once);
        _delivery.Verify(d => d.PushDueAsync(It.IsAny<IReadOnlyCollection<NetworkDiagnosticAssignment>>(), It.IsAny<DateTime>()), Times.Once);
        _delivery.Verify(d => d.PushAsync(It.IsAny<NetworkDiagnosticAssignment>(), It.IsAny<NetworkDiagnosticRun>()), Times.Never);
    }

    [Test]
    public async Task Create_Users_NobodyOnline_PushesNothing()
    {
        KnownPlayers("Name#1234");

        var result = await _service.CreateRunAsync(Request(new RunAudience { Kind = AudienceKind.Users, BattleTags = ["Name#1234"] }), "Admin#1");

        Assert.AreEqual(201, result.StatusCode);
        _delivery.VerifyNoOtherCalls();
    }

    // The run and its assignments are stored by then: a 500 would make the admin create the same run twice.
    [Test]
    public async Task Create_Users_PushFailure_StillAnswers201()
    {
        KnownPlayers("Name#1234");
        Online("Name#1234");
        _delivery.Setup(d => d.PushDueAsync(It.IsAny<IReadOnlyCollection<NetworkDiagnosticAssignment>>(), It.IsAny<DateTime>()))
            .ThrowsAsync(new InvalidOperationException("hub down"));

        var result = await _service.CreateRunAsync(Request(new RunAudience { Kind = AudienceKind.Users, BattleTags = ["Name#1234"] }), "Admin#1");

        Assert.AreEqual(201, result.StatusCode);
        _runs.Verify(r => r.InsertAsync(It.IsAny<NetworkDiagnosticRun>()), Times.Once);
    }

    [Test]
    public async Task Create_NullBody_400()
    {
        var result = await _service.CreateRunAsync(null, "Admin#1");

        Assert.AreEqual(400, result.StatusCode);
        Assert.AreEqual("""{"error":"body is required"}""", ErrorOf(result));
    }

    /// <summary>
    /// The launcher (tauri-plugin-netdiag request.rs) drops a WHOLE push when an endpoint's label, targetKey or ip is
    /// null, a tcpPort is outside 1-65535, windowSeconds is outside 30-300, no known method is left, the measurementId
    /// is not 1-64 chars of [0-9a-fA-F-] or the token is not 43 base64url chars. Everything a run stores must pass.
    /// </summary>
    [Test]
    public async Task Create_EverythingStored_SatisfiesTheLauncherRequestRules()
    {
        KnownPlayers("Name#1234");
        var config = new NetDiagTargetsConfig(
            [new CoreServerConfig { Key = "nolabel", Ip = "203.0.113.10", TcpPort = 65535 }],
            [new BaselineConfig { Key = "blank", Label = " ", Ip = "1.1.1.1", TcpPort = 1 }], "/geoip/c.mmdb", "/geoip/a.mmdb");
        NetworkDiagnosticRun stored = null;
        _runs.Setup(r => r.InsertAsync(It.IsAny<NetworkDiagnosticRun>())).Callback<NetworkDiagnosticRun>(r => stored = r).Returns(Task.CompletedTask);
        var targets = new RunTargets
        {
            FloNodeIds = [12, 12],
            CoreServers = ["nolabel"],
            Baselines = ["blank"],
            CustomTargets = [new CustomTarget { Ip = "10.0.0.5" }, new CustomTarget { Ip = "10.0.0.5", TcpPort = 443 }, new CustomTarget { Ip = "192.168.1.1", TcpPort = 65535 }],
        };
        var request = Request(new RunAudience { Kind = AudienceKind.Users, BattleTags = ["Name#1234"] }, targets) with
        {
            Methods = ["IcmpMtr", "IcmpMtr", "FloUdpPing"],
            WindowSeconds = null,
        };

        var result = await ServiceWith(config).CreateRunAsync(request, "Admin#1");

        Assert.AreEqual(201, result.StatusCode);
        Assert.AreEqual(new[] { "core:nolabel", "baseline:blank", "custom:10.0.0.5:443", "custom:192.168.1.1:65535" }, stored.ResolvedEndpoints.Select(e => e.TargetKey));
        Assert.AreEqual(new[] { "nolabel", "blank", "10.0.0.5:443", "192.168.1.1:65535" }, stored.ResolvedEndpoints.Select(e => e.Label));
        foreach (var e in stored.ResolvedEndpoints)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(e.TargetKey), "targetKey");
            Assert.IsFalse(string.IsNullOrWhiteSpace(e.Label), $"label of {e.TargetKey}");
            Assert.IsTrue(NetDiagIp.IsStrictIpv4(e.Ip), $"ip of {e.TargetKey}");
            Assert.That(e.TcpPort, Is.InRange(1, 65535), $"tcpPort of {e.TargetKey}");
        }
        Assert.That(stored.WindowSeconds, Is.InRange(30, 300));
        Assert.AreEqual(new[] { "IcmpMtr", "FloUdpPing" }, stored.Methods);
        Assert.IsTrue(stored.Methods.All(NetDiagConstants.AllMethods.Contains));
        Assert.AreEqual(new[] { 12 }, stored.Targets.FloNodeIds);
        Assert.IsTrue(_inserted.All(a => Regex.IsMatch(a.Id, "^[0-9a-fA-F-]{1,64}$")), "measurementId format");
        // Booleans only: a failure message must never print a token.
        Assert.IsTrue(_inserted.All(a => Regex.IsMatch(a.Token, "^[A-Za-z0-9_-]{43}$")), "token format");
    }

    [TestCase(0)]
    [TestCase(65536)]
    [TestCase(-1)]
    public async Task Create_CustomTargetPortOutOfRange_400(int port)
    {
        var targets = new RunTargets { CustomTargets = [new CustomTarget { Ip = "10.0.0.5", TcpPort = port }] };

        var result = await _service.CreateRunAsync(Request(new RunAudience { Kind = AudienceKind.All }, targets), "Admin#1");

        Assert.AreEqual("""{"error":"custom target port must be in 1-65535"}""", ErrorOf(result));
        _runs.Verify(r => r.InsertAsync(It.IsAny<NetworkDiagnosticRun>()), Times.Never);
    }

    [TestCase(29)]
    [TestCase(301)]
    [TestCase(0)]
    [TestCase(-60)]
    public async Task Create_WindowOutsideTheLauncherRange_400(int window)
    {
        var result = await _service.CreateRunAsync(Request(new RunAudience { Kind = AudienceKind.All }) with { WindowSeconds = window }, "Admin#1");

        Assert.AreEqual(400, result.StatusCode);
        _runs.Verify(r => r.InsertAsync(It.IsAny<NetworkDiagnosticRun>()), Times.Never);
    }

    [Test]
    public async Task Create_Users_UnknownTags_400WithTheList_AndWritesNothing()
    {
        KnownPlayers("Name#1234");
        _settings.Setup(s => s.LoadMany(It.IsAny<string[]>())).ReturnsAsync([new PersonalSetting("Settings#1111")]);

        var result = await _service.CreateRunAsync(
            Request(new RunAudience { Kind = AudienceKind.Users, BattleTags = ["Name#1234", "Settings#1111", "Ghost#9999"] }), "Admin#1");

        Assert.AreEqual(400, result.StatusCode);
        Assert.AreEqual("""{"error":"unknown_battletags","battleTags":["Ghost#9999"]}""", ErrorOf(result));
        _runs.Verify(r => r.InsertAsync(It.IsAny<NetworkDiagnosticRun>()), Times.Never);
        Assert.IsEmpty(_inserted);
    }

    [Test]
    public async Task Create_SampleCount_PicksFromOnlineUsers_WithSlotsInTheWindow()
    {
        Online("A#1111", "B#2222", "C#3333");
        NetworkDiagnosticRun stored = null;
        _runs.Setup(r => r.InsertAsync(It.IsAny<NetworkDiagnosticRun>())).Callback<NetworkDiagnosticRun>(r => stored = r).Returns(Task.CompletedTask);

        var result = await _service.CreateRunAsync(Request(new RunAudience { Kind = AudienceKind.SampleCount, Count = 2 }), "Admin#1");

        Assert.AreEqual(201, result.StatusCode);
        Assert.AreEqual(Now, stored.WindowStart);
        Assert.AreEqual(Now.AddMinutes(30), stored.WindowEnd);
        Assert.AreEqual(2, _inserted.Count);
        Assert.IsTrue(_inserted.All(a => a.DeliverAfter >= Now && a.DeliverAfter <= Now.AddMinutes(30)));
        Assert.IsTrue(_inserted.All(a => a.DeliverBy == Now.AddMinutes(30) + NetDiagConstants.FleetSubmitGrace));
        // Fleet slots are spread over the window: the dispatcher pushes them when they are due.
        _delivery.VerifyNoOtherCalls();
    }

    [Test]
    public async Task Create_All_NobodyOnline_StillCreatesTheRunForLateJoiners()
    {
        var result = await _service.CreateRunAsync(Request(new RunAudience { Kind = AudienceKind.All }), "Admin#1");

        Assert.AreEqual(201, result.StatusCode);
        Assert.AreEqual(0, ((RunDto)result.Body).Counters.Assigned);
    }

    [Test]
    public async Task Create_Invalid_400WithValidatorMessage()
    {
        var result = await _service.CreateRunAsync(Request(new RunAudience { Kind = AudienceKind.All }) with { WindowSeconds = 10 }, "Admin#1");

        Assert.AreEqual(400, result.StatusCode);
        Assert.AreEqual("""{"error":"windowSeconds must be in 30-300"}""", ErrorOf(result));
    }

    [Test]
    public async Task Create_UnknownCoreKey_400()
    {
        var result = await _service.CreateRunAsync(Request(new RunAudience { Kind = AudienceKind.All }, new RunTargets { CoreServers = ["nope"] }), "Admin#1");

        Assert.AreEqual("""{"error":"unknown_targets","targetKeys":["core:nope"]}""", ErrorOf(result));
    }

    [Test]
    public async Task Create_UnknownFloNode_400_AndMatchmakingDown_502()
    {
        var unknown = await _service.CreateRunAsync(Request(new RunAudience { Kind = AudienceKind.All }, new RunTargets { FloNodeIds = [12, 99] }), "Admin#1");
        Assert.AreEqual("""{"error":"unknown_flo_nodes","floNodeIds":[99]}""", ErrorOf(unknown));

        _floNodes.Setup(f => f.GetFloNodesAsync()).ThrowsAsync(new HttpRequestException("down"));
        var down = await _service.CreateRunAsync(Request(new RunAudience { Kind = AudienceKind.All }, new RunTargets { AllFloNodes = true }), "Admin#1");
        Assert.AreEqual(502, down.StatusCode);
        Assert.AreEqual("""{"error":"matchmaking_unavailable"}""", ErrorOf(down));
        _runs.Verify(r => r.InsertAsync(It.IsAny<NetworkDiagnosticRun>()), Times.Never);
    }

    // HttpClient reports its own timeout as TaskCanceledException(TimeoutException), not as HttpRequestException.
    [Test]
    public async Task MatchmakingTimeout_Is502_ForCreateAndTargets()
    {
        _floNodes.Setup(f => f.GetFloNodesAsync()).ThrowsAsync(new TaskCanceledException("timeout", new TimeoutException()));

        var create = await _service.CreateRunAsync(Request(new RunAudience { Kind = AudienceKind.All }, new RunTargets { FloNodeIds = [12] }), "Admin#1");
        var targets = await _service.GetTargetsAsync();

        Assert.AreEqual("""{"error":"matchmaking_unavailable"}""", ErrorOf(create));
        Assert.AreEqual(502, targets.StatusCode);
        Assert.AreEqual("""{"error":"matchmaking_unavailable"}""", ErrorOf(targets));
    }

    [Test]
    public async Task GetTargets_MatchmakingDown_502()
    {
        _floNodes.Setup(f => f.GetFloNodesAsync()).ThrowsAsync(new HttpRequestException("down"));

        var result = await _service.GetTargetsAsync();

        Assert.AreEqual(502, result.StatusCode);
        Assert.AreEqual("""{"error":"matchmaking_unavailable"}""", ErrorOf(result));
    }

    [Test]
    public async Task Create_WithoutFloTargets_NeverAsksMatchmaking()
    {
        var result = await _service.CreateRunAsync(Request(new RunAudience { Kind = AudienceKind.All }), "Admin#1");

        Assert.AreEqual(201, result.StatusCode);
        _floNodes.Verify(f => f.GetFloNodesAsync(), Times.Never);
    }

    [Test]
    public async Task Create_AllFloNodes_CountsOnlyEnabledNodesTowardsTheTargetLimit()
    {
        _floNodes.Setup(f => f.GetFloNodesAsync()).ReturnsAsync(
            Enumerable.Range(1, 150).Select(i => new FloNodeInfo { Id = i, Name = $"n{i}" })
                .Concat(Enumerable.Range(151, 100).Select(i => new FloNodeInfo { Id = i, Name = $"n{i}", Settings = new FloNodeInfoSettings { IsDisabled = true } }))
                .ToList());
        var targets = new RunTargets { AllFloNodes = true, CustomTargets = Enumerable.Range(1, 49).Select(i => new CustomTarget { Ip = $"10.0.0.{i}" }).ToList(), IncludeDefaultGateway = true };

        var result = await _service.CreateRunAsync(Request(new RunAudience { Kind = AudienceKind.All }, targets), "Admin#1");

        Assert.AreEqual(201, result.StatusCode, "150 enabled nodes + 49 custom + gateway = 200");
    }

    [Test]
    public async Task Create_MoreThan200ResolvedTargets_400()
    {
        _floNodes.Setup(f => f.GetFloNodesAsync()).ReturnsAsync(
            Enumerable.Range(1, 160).Select(i => new FloNodeInfo { Id = i, Name = $"n{i}" }).ToList());
        var targets = new RunTargets
        {
            AllFloNodes = true,
            CustomTargets = Enumerable.Range(1, 45).Select(i => new CustomTarget { Ip = $"10.0.0.{i}" }).ToList(),
        };

        var result = await _service.CreateRunAsync(Request(new RunAudience { Kind = AudienceKind.All }, targets), "Admin#1");

        Assert.AreEqual("""{"error":"too many targets (max 200)"}""", ErrorOf(result));
    }

    [Test]
    public async Task GetTargets_MapsNodesAndConfig()
    {
        var result = await _service.GetTargetsAsync();

        var dto = (TargetsDto)result.Body;
        Assert.AreEqual(2, dto.FloNodes.Count);
        Assert.IsTrue(dto.FloNodes.Single(n => n.Id == 14).IsDisabled);
        Assert.AreEqual("bigboi", dto.CoreServers.Single().Key);
        Assert.AreEqual(6, dto.Baselines.Count);
    }

    [Test]
    public async Task Cancel_ExpiresPending_ThenSecondCancelIs409_AndMissingIs404()
    {
        var run = new NetworkDiagnosticRun { Id = "r1", Audience = new RunAudience { Kind = AudienceKind.All }, Targets = new RunTargets(), WindowSeconds = 60, CancelledAt = Now };
        _runs.SetupSequence(r => r.TryCancelAsync("r1", Now)).ReturnsAsync(run).ReturnsAsync((NetworkDiagnosticRun)null);
        _runs.Setup(r => r.GetAsync("r1")).ReturnsAsync(run);
        _assignments.Setup(a => a.ExpireAllPendingAsync("r1")).ReturnsAsync(4);

        Assert.AreEqual(200, (await _service.CancelRunAsync("r1")).StatusCode);
        _runs.Verify(r => r.IncrementCountersAsync("r1", 0, 0, 0, 4), Times.Once);
        Assert.AreEqual(409, (await _service.CancelRunAsync("r1")).StatusCode);
        Assert.AreEqual(404, (await _service.CancelRunAsync("missing")).StatusCode);
    }

    // Tasks 9/10 close their cancel races by re-reading the run after an insert: that only works when the cancel
    // marks the run BEFORE it sweeps the pending assignments, and when the sweep's count lands in Counters.Expired.
    [Test]
    public async Task Cancel_MarksTheRunCancelled_ThenExpiresPending_ThenCountsThem()
    {
        var calls = new List<string>();
        var run = new NetworkDiagnosticRun { Id = "r1", Audience = new RunAudience { Kind = AudienceKind.All }, Targets = new RunTargets(), WindowSeconds = 60, CancelledAt = Now };
        _runs.Setup(r => r.TryCancelAsync("r1", Now)).Callback(() => calls.Add("cancel")).ReturnsAsync(run);
        _assignments.Setup(a => a.ExpireAllPendingAsync("r1")).Callback(() => calls.Add("expire")).ReturnsAsync(3);
        _runs.Setup(r => r.IncrementCountersAsync("r1", 0, 0, 0, 3)).Callback(() => calls.Add("count")).Returns(Task.CompletedTask);
        _runs.Setup(r => r.GetAsync("r1")).Callback(() => calls.Add("read"))
            .ReturnsAsync(run with { Counters = new RunCounters { Assigned = 5, Expired = 3 } });

        var result = await _service.CancelRunAsync("r1");

        Assert.AreEqual(new[] { "cancel", "expire", "count", "read" }, calls);
        var dto = (RunDto)result.Body;
        Assert.AreEqual(Now, dto.CancelledAt);
        Assert.AreEqual(3, dto.Counters.Expired, "the answer carries the counters after the sweep");
    }

    // The sweep of a cancel failed after CancelledAt was set: the run is cancelled but its assignments are still
    // Pending (and a delivered one could still be submitted, against spec section 5.4). The admin's retry finishes
    // the sweep; it still answers 409, and further retries never count the same assignments again.
    [Test]
    public async Task Cancel_WhoseSweepFailed_IsCompletedByTheRetry_AndCountedOnce()
    {
        var run = new NetworkDiagnosticRun { Id = "r1", Audience = new RunAudience { Kind = AudienceKind.All }, Targets = new RunTargets(), WindowSeconds = 60, CancelledAt = Now };
        _runs.SetupSequence(r => r.TryCancelAsync("r1", Now)).ReturnsAsync(run).ReturnsAsync((NetworkDiagnosticRun)null).ReturnsAsync((NetworkDiagnosticRun)null);
        _runs.Setup(r => r.GetAsync("r1")).ReturnsAsync(run);
        _assignments.SetupSequence(a => a.ExpireAllPendingAsync("r1"))
            .ThrowsAsync(new TimeoutException("mongo"))
            .ReturnsAsync(4)
            .ReturnsAsync(0);
        var service = ServiceWith(_config, RealDelivery());

        Assert.ThrowsAsync<TimeoutException>(() => service.CancelRunAsync("r1"));
        _runs.Verify(r => r.IncrementCountersAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);

        var retry = await service.CancelRunAsync("r1");
        var again = await service.CancelRunAsync("r1");

        Assert.AreEqual((409, """{"error":"already_cancelled"}"""), (retry.StatusCode, ErrorOf(retry)));
        Assert.AreEqual((409, """{"error":"already_cancelled"}"""), (again.StatusCode, ErrorOf(again)));
        _assignments.Verify(a => a.ExpireAllPendingAsync("r1"), Times.Exactly(3));
        _runs.Verify(r => r.IncrementCountersAsync("r1", 0, 0, 0, 4), Times.Once);
        _runs.Verify(r => r.IncrementCountersAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Once);
    }

    // The retry's sweep fails as well: still a failure the admin can retry, never a 409 that hides Pending leftovers.
    [Test]
    public void Cancel_RetryWhoseSweepFailsAgain_IsStillAFailure()
    {
        var run = new NetworkDiagnosticRun { Id = "r1", Audience = new RunAudience { Kind = AudienceKind.All }, Targets = new RunTargets(), WindowSeconds = 60, CancelledAt = Now };
        _runs.Setup(r => r.TryCancelAsync("r1", Now)).ReturnsAsync((NetworkDiagnosticRun)null);
        _runs.Setup(r => r.GetAsync("r1")).ReturnsAsync(run);
        _assignments.Setup(a => a.ExpireAllPendingAsync("r1")).ThrowsAsync(new TimeoutException("mongo"));

        Assert.ThrowsAsync<TimeoutException>(() => ServiceWith(_config, RealDelivery()).CancelRunAsync("r1"));
    }

    // Assignments are stored before the run. When the run insert fails they belong to no run: they are expired at
    // once so they never reach a launcher or the dispatcher's push batch, and the admin sees the original failure.
    [Test]
    public void Create_RunInsertFails_ExpiresTheOrphanAssignments_AndRethrows()
    {
        KnownPlayers("Name#1234", "Other#5678");
        Online("Name#1234");
        var calls = new List<string>();
        _runs.Setup(r => r.InsertAsync(It.IsAny<NetworkDiagnosticRun>())).Callback(() => calls.Add("insert run")).ThrowsAsync(new TimeoutException("run insert"));
        _assignments.Setup(a => a.ExpireAllPendingAsync(It.IsAny<string>())).Callback<string>(id => calls.Add("expire " + id)).ReturnsAsync(2);

        var thrown = Assert.ThrowsAsync<TimeoutException>(
            () => _service.CreateRunAsync(Request(new RunAudience { Kind = AudienceKind.Users, BattleTags = ["Name#1234", "Other#5678"] }), "Admin#1"));

        Assert.AreEqual("run insert", thrown.Message);
        Assert.AreEqual(2, _inserted.Count);
        Assert.AreEqual(new[] { "insert run", "expire " + _inserted[0].RunId }, calls);
        // No run exists, so there is no counter to update and nothing to push.
        _runs.Verify(r => r.IncrementCountersAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        _delivery.VerifyNoOtherCalls();
    }

    [Test]
    public void Create_RunInsertFails_AndTheCleanupFailsToo_TheOriginalExceptionSurfaces_AndTheCleanupIsLogged()
    {
        KnownPlayers("Name#1234");
        var log = new ListLogger<NetworkDiagnosticsRunService>();
        _runs.Setup(r => r.InsertAsync(It.IsAny<NetworkDiagnosticRun>())).ThrowsAsync(new TimeoutException("run insert"));
        _assignments.Setup(a => a.ExpireAllPendingAsync(It.IsAny<string>())).ThrowsAsync(new InvalidOperationException("cleanup"));

        var thrown = Assert.ThrowsAsync<TimeoutException>(
            () => ServiceWith(_config, log: log).CreateRunAsync(Request(new RunAudience { Kind = AudienceKind.Users, BattleTags = ["Name#1234"] }), "Admin#1"));

        Assert.AreEqual("run insert", thrown.Message);
        Assert.AreEqual(1, log.Count(LogLevel.Error));
        StringAssert.Contains(_inserted[0].RunId, log.AllText);
        StringAssert.DoesNotContain(_inserted[0].Token, log.AllText);
    }

    [Test]
    public async Task Cancel_NothingPending_LeavesTheCountersAlone()
    {
        var run = new NetworkDiagnosticRun { Id = "r1", Audience = new RunAudience { Kind = AudienceKind.All }, Targets = new RunTargets(), WindowSeconds = 60, CancelledAt = Now };
        _runs.Setup(r => r.TryCancelAsync("r1", Now)).ReturnsAsync(run);
        _assignments.Setup(a => a.ExpireAllPendingAsync("r1")).ReturnsAsync(0);

        Assert.AreEqual(200, (await _service.CancelRunAsync("r1")).StatusCode);

        _runs.Verify(r => r.IncrementCountersAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task Cancel_MissingRun_NeverTouchesAssignments()
    {
        var result = await _service.CancelRunAsync("missing");

        Assert.AreEqual("""{"error":"not_found"}""", ErrorOf(result));
        _assignments.Verify(a => a.ExpireAllPendingAsync(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task GetRun_IncludesAssignmentsWithClientGeoOfSubmittedOnes()
    {
        var run = new NetworkDiagnosticRun { Id = "r1", Audience = new RunAudience { Kind = AudienceKind.Users }, Targets = new RunTargets(), WindowSeconds = 60 };
        var submitted = AssignmentFactory.Create("r1", "A#1111", Now, Now.AddDays(14)) with { Status = AssignmentStatus.Submitted, SubmittedAt = Now };
        var pending = AssignmentFactory.Create("r1", "B#2222", Now, Now.AddDays(14));
        _runs.Setup(r => r.GetAsync("r1")).ReturnsAsync(run);
        _assignments.Setup(a => a.ForRunAsync("r1")).ReturnsAsync([submitted, pending]);
        _results.Setup(r => r.ClientGeoByMeasurementAsync(It.Is<IReadOnlyCollection<string>>(ids => ids.Single() == submitted.Id)))
            .ReturnsAsync(new Dictionary<string, ClientGeo> { [submitted.Id] = new() { Country = "DE" } });

        var detail = (RunDetailDto)(await _service.GetRunAsync("r1")).Body;

        Assert.AreEqual("r1", detail.Run.Id);
        Assert.AreEqual("DE", detail.Assignments.Single(a => a.MeasurementId == submitted.Id).ClientGeo.Country);
        Assert.IsNull(detail.Assignments.Single(a => a.MeasurementId == pending.Id).ClientGeo);
        Assert.AreEqual(404, (await _service.GetRunAsync("missing")).StatusCode);

        // A10: the run detail (admin + LLM endpoint) never exposes a measurement token.
        var json = JsonSerializer.Serialize(detail, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        StringAssert.DoesNotContain(submitted.Token, json);
        StringAssert.DoesNotContain(pending.Token, json);
        StringAssert.DoesNotContain("\"token", json);
    }

    // A10: no admin DTO type has a token-like property, so a future mapping cannot leak one by accident.
    [TestCase(typeof(AssignmentDto))]
    [TestCase(typeof(RunDto))]
    [TestCase(typeof(RunDetailDto))]
    public void AdminDtos_HaveNoTokenProperty(Type dto) =>
        Assert.IsEmpty(dto.GetProperties().Where(p => p.Name.Contains("Token", StringComparison.OrdinalIgnoreCase)).Select(p => p.Name));

    [Test]
    public async Task ListRuns_PassesFiltersAndZeroBasedPageThrough_AndMapsThePage()
    {
        var from = Now.AddDays(-1);
        var run = new NetworkDiagnosticRun { Id = "r1", Audience = new RunAudience { Kind = AudienceKind.All }, Targets = new RunTargets(), WindowSeconds = 60 };
        _runs.Setup(r => r.ListAsync("Admin#1", from, Now, 2, 10)).ReturnsAsync((new List<NetworkDiagnosticRun> { run }, 21L));

        var page = (PageDto<RunDto>)(await _service.ListRunsAsync("Admin#1", from, Now, 2, 10)).Body;

        Assert.AreEqual(21, page.Total);
        Assert.AreEqual("r1", page.Items.Single().Id);
        Assert.AreEqual("""{"items":[],"total":0}""", JsonSerializer.Serialize(new PageDto<RunDto>([], 0), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [TestCase(-1, 25, "page must be >= 0")]
    [TestCase(0, 0, "pageSize must be in 1-100")]
    [TestCase(0, 101, "pageSize must be in 1-100")]
    [TestCase(int.MaxValue, 100, "page is too large")] // page * pageSize would overflow the repository's Skip
    [TestCase(NetDiagPaging.MaxPage + 1, 1, "page is too large")]
    public async Task ListRuns_RejectsBadPaging(int page, int pageSize, string error)
    {
        var result = await _service.ListRunsAsync(null, null, null, page, pageSize);

        Assert.AreEqual(400, result.StatusCode);
        Assert.AreEqual($$"""{"error":"{{error}}"}""", ErrorOf(result));
        _runs.Verify(r => r.ListAsync(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [TestCase(0, 1)]
    [TestCase(0, 100)]
    [TestCase(NetDiagPaging.MaxPage, 100)]
    public void Paging_AcceptsTheBounds(int page, int pageSize) => Assert.IsNull(NetDiagPaging.Validate(page, pageSize));
}

[TestFixture]
public class NetworkDiagnosticsRunServiceRegistrationTests
{
    // The run service creates assignments, so it sees measurement tokens (A10): it must be the plain class, never a
    // Castle tracing proxy that would record arguments and return values.
    [Test]
    public void AddNetworkDiagnostics_ResolvesThePlainRunService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignalR();
        services.AddSingleton<ConnectionMapping>();
        services.AddNetworkDiagnostics();
        services.AddSingleton(new Mock<INetworkDiagnosticsRunRepository>().Object);
        services.AddSingleton(new Mock<INetworkDiagnosticsAssignmentRepository>().Object);
        services.AddSingleton(new Mock<INetworkDiagnosticsResultRepository>().Object);
        services.AddSingleton(new Mock<IFloNodeSource>().Object);
        services.AddSingleton(new Mock<IPlayerRepository>().Object);
        services.AddSingleton(new Mock<IPersonalSettingsRepository>().Object);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var service = provider.GetRequiredService<INetworkDiagnosticsRunService>();

        Assert.AreEqual(typeof(NetworkDiagnosticsRunService), service.GetType());
        Assert.AreEqual(1, services.Count(d => d.ServiceType == typeof(NetDiagTargetsConfig)), "the targets config is registered once (Task 7)");
    }
}
