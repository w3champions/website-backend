using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Serilog.Events;
using W3ChampionsStatisticService.Services;

namespace WC3ChampionsStatisticService.Tests.Services;

/// <summary>
/// Deterministic: time only moves via <see cref="FakeTimeProvider.Advance"/> (which also fires the roster's
/// FirstLoadWait timer), and fetches are gated with TaskCompletionSources. No real delays or timeouts.
/// "Does not wait" is asserted as a synchronously completed task, never by racing a clock.
/// </summary>
[TestFixture]
public class AdminRosterTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private Mock<IdentityServiceClient> _identityClientMock;
    private FakeTimeProvider _clock;
    private AdminRoster _roster;

    [SetUp]
    public void SetUp()
    {
        _identityClientMock = new Mock<IdentityServiceClient>(new WC3ChampionsStatisticService.Tests.Admin.StubHttpClientFactory(new HttpClient()));
        _clock = new FakeTimeProvider(Start);
        _roster = new AdminRoster(_identityClientMock.Object, _clock);
    }

    private void SetupTags(params string[] tags) =>
        _identityClientMock.Setup(c => c.GetAdminBattleTags()).ReturnsAsync(tags.ToList());

    private void SetupFailure() =>
        _identityClientMock.Setup(c => c.GetAdminBattleTags()).ThrowsAsync(new HttpRequestException("boom"));

    private TaskCompletionSource<List<string>> _gateEntered;

    /// <summary>The fetch blocks until the returned gate is completed; <see cref="GateEntered"/> fires when it starts.</summary>
    private TaskCompletionSource<List<string>> SetupGate()
    {
        var gate = new TaskCompletionSource<List<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _gateEntered = new TaskCompletionSource<List<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _identityClientMock.Setup(c => c.GetAdminBattleTags())
            .Callback(() => _gateEntered.TrySetResult(null))
            .Returns(gate.Task);
        return gate;
    }

    private Task GateEntered() => _gateEntered.Task;

    private void Advance(TimeSpan by) => _clock.Advance(by);

    /// <summary>Lets the background refresh started by the previous call finish.</summary>
    private Task Settle() => _roster.PendingRefresh;

    private async Task<bool> IsAdmin(string battleTag) => (await _roster.GetSnapshotAsync()).IsAdmin(battleTag);

    private async Task LoadInitial(params string[] tags)
    {
        SetupTags(tags);
        await _roster.GetSnapshotAsync();
        await Settle();
    }

    [Test]
    public async Task FirstCall_LoadsFromIdentityService()
    {
        SetupTags("Admin#1");

        Assert.IsTrue(await IsAdmin("Admin#1"));
        Assert.IsFalse(await IsAdmin("Other#2"));
    }

    [Test]
    public async Task IsAdmin_IsCaseInsensitive()
    {
        SetupTags("Admin#1");

        Assert.IsTrue(await IsAdmin("ADMIN#1"));
        var snapshot = await _roster.GetSnapshotAsync();
        Assert.IsTrue(snapshot.Tags.Contains("admin#1"));
    }

    [Test]
    public async Task WithinTtl_DoesNotRefetch()
    {
        await LoadInitial("Admin#1");

        Advance(AdminRoster.Ttl - TimeSpan.FromSeconds(1));
        await IsAdmin("Admin#1");
        await _roster.GetSnapshotAsync();
        await Settle();

        _identityClientMock.Verify(c => c.GetAdminBattleTags(), Times.Once);
    }

    [Test]
    public async Task AfterTtl_ServesStaleSetThenNewSetOnceBackgroundRefreshCompletes()
    {
        await LoadInitial("Admin#1");

        SetupTags("New#2");
        Advance(AdminRoster.Ttl);

        // Stale-while-revalidate: the expired set is served while the refresh runs.
        var gate = SetupGate();
        var stale = IsAdmin("Admin#1");
        Assert.IsTrue(stale.IsCompleted, "served without waiting for the refresh");
        Assert.IsTrue(await stale);
        gate.SetResult(new List<string> { "New#2" });
        await Settle();

        Assert.IsFalse(await IsAdmin("Admin#1"));
        Assert.IsTrue(await IsAdmin("New#2"));
    }

    [Test]
    public async Task Loaded_GatedRefresh_DoesNotDelayCallers()
    {
        await LoadInitial("Admin#1");
        var gate = SetupGate();
        Advance(AdminRoster.Ttl);

        // The refresh never completes; callers must not wait for it.
        var isAdmin = IsAdmin("Admin#1");
        var tags = _roster.GetSnapshotAsync();
        Assert.IsTrue(isAdmin.IsCompleted, "IsAdmin completed synchronously");
        Assert.IsTrue(tags.IsCompleted, "GetSnapshotAsync completed synchronously");
        Assert.IsTrue(await isAdmin);
        Assert.IsTrue((await tags).Tags.Contains("Admin#1"));
        Assert.IsFalse(_roster.PendingRefresh.IsCompleted, "refresh still in flight");

        gate.SetResult(new List<string> { "Admin#1" });
        await Settle();
    }

    [Test]
    public async Task Loaded_ManyCallersDuringRefresh_OnlyOneRefreshInFlight()
    {
        await LoadInitial("Admin#1");
        var gate = SetupGate();
        Advance(AdminRoster.Ttl);

        var calls = Enumerable.Range(0, 20).Select(_ => Task.Run(() => IsAdmin("Admin#1"))).ToList();
        await Task.WhenAll(calls);
        await GateEntered();
        Advance(TimeSpan.FromSeconds(1));
        await _roster.GetSnapshotAsync();

        // 1 initial load + exactly 1 background refresh, still blocked on the gate.
        _identityClientMock.Verify(c => c.GetAdminBattleTags(), Times.Exactly(2));

        gate.SetResult(new List<string> { "Admin#1" });
        await Settle();
        await _roster.GetSnapshotAsync();
        await Settle();
        _identityClientMock.Verify(c => c.GetAdminBattleTags(), Times.Exactly(2), "fresh again after the refresh");
    }

    [Test]
    public async Task RefreshFailure_ServesLastGoodSet()
    {
        await LoadInitial("Admin#1");

        SetupFailure();
        Advance(AdminRoster.Ttl);
        await IsAdmin("Admin#1");
        await Settle();

        Assert.IsTrue(await IsAdmin("Admin#1"));
    }

    [Test]
    public async Task RefreshFailure_RetriesOnlyAfterFailureRetryDelay()
    {
        await LoadInitial("Admin#1");

        SetupFailure();
        Advance(AdminRoster.Ttl);
        await IsAdmin("Admin#1");
        await Settle();
        _identityClientMock.Verify(c => c.GetAdminBattleTags(), Times.Exactly(2));

        Advance(AdminRoster.FailureRetryDelay - TimeSpan.FromSeconds(1));
        await IsAdmin("Admin#1");
        await Settle();
        _identityClientMock.Verify(c => c.GetAdminBattleTags(), Times.Exactly(2));

        Advance(TimeSpan.FromSeconds(1));
        await IsAdmin("Admin#1");
        await Settle();
        _identityClientMock.Verify(c => c.GetAdminBattleTags(), Times.Exactly(3));
    }

    [Test]
    public async Task NeverLoaded_FailureFailsOpenAsNoAdmins()
    {
        _identityClientMock.Setup(c => c.GetAdminBattleTags()).ThrowsAsync(new TaskCanceledException());

        Assert.IsFalse(await IsAdmin("Admin#1"));
        Assert.That(await _roster.GetSnapshotAsync(), Is.SameAs(AdminSnapshot.Empty));
    }

    [Test]
    public async Task NeverLoaded_SlowFirstLoad_FailsOpenExactlyAtFirstLoadWait()
    {
        var gate = SetupGate();

        var pending = IsAdmin("Admin#1");
        await GateEntered();
        Assert.IsFalse(pending.IsCompleted, "waits for the in-flight first load");

        Advance(AdminRoster.FirstLoadWait - TimeSpan.FromTicks(1));
        Assert.IsFalse(pending.IsCompleted, "still waiting just before FirstLoadWait");

        Advance(TimeSpan.FromTicks(1));
        Assert.IsTrue(pending.IsCompleted, "the wait is driven by the injected TimeProvider, not wall-clock time");
        Assert.IsFalse(await pending, "fails open (no admins) once FirstLoadWait elapses");
        Assert.IsFalse(_roster.PendingRefresh.IsCompleted, "the load keeps running");

        // The load keeps running in the background and is picked up once it lands.
        gate.SetResult(new List<string> { "Admin#1" });
        await Settle();
        Assert.IsTrue(await IsAdmin("Admin#1"));
        _identityClientMock.Verify(c => c.GetAdminBattleTags(), Times.Once);
    }

    [Test]
    public void FirstLoadWait_IsWellUnderChatServiceTimeout()
    {
        Assert.That(AdminRoster.FirstLoadWait, Is.LessThanOrEqualTo(TimeSpan.FromMilliseconds(500)));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    public void Snapshot_BlankTag_IsNeverAdmin(string tag)
    {
        Assert.IsFalse(new AdminSnapshot(["Admin#1", "", "  "]).IsAdmin(tag));
    }

    [Test]
    public void Snapshot_DropsBlankTags()
    {
        Assert.That(new AdminSnapshot(["Admin#1", null, "", "  "]).Tags, Is.EquivalentTo(new[] { "Admin#1" }));
    }

    [Test]
    public async Task Snapshot_IsUnchangedByALaterRefresh()
    {
        await LoadInitial("Admin#1");
        var before = await _roster.GetSnapshotAsync();

        SetupTags("New#2");
        Advance(AdminRoster.Ttl);
        await _roster.GetSnapshotAsync();
        await Settle();
        var after = await _roster.GetSnapshotAsync();

        Assert.That(before.Tags, Is.EquivalentTo(new[] { "Admin#1" }), "an operation's snapshot never changes under it");
        Assert.IsTrue(before.IsAdmin("Admin#1"));
        Assert.IsFalse(before.IsAdmin("New#2"));
        Assert.That(after.Tags, Is.EquivalentTo(new[] { "New#2" }));
    }

    [Test]
    public async Task NeverLoaded_ConcurrentFirstCalls_ShareOneFetch()
    {
        var gate = SetupGate();

        // Every caller is started while the single fetch is provably still blocked on the gate.
        var calls = Enumerable.Range(0, 5).Select(_ => IsAdmin("Admin#1")).ToList();
        await GateEntered();
        Assert.IsFalse(_roster.PendingRefresh.IsCompleted);
        _identityClientMock.Verify(c => c.GetAdminBattleTags(), Times.Once);

        // No time passes, so FirstLoadWait can't expire: every caller gets the loaded set.
        Assert.IsFalse(calls.Any(c => c.IsCompleted));
        gate.SetResult(new List<string> { "Admin#1" });
        var results = await Task.WhenAll(calls);

        Assert.IsTrue(results.All(r => r));
        _identityClientMock.Verify(c => c.GetAdminBattleTags(), Times.Once);
    }

    [Test]
    public async Task RefreshFailures_FirstLoggedAsWarning_RepeatsAsDebug_ResetOnSuccess()
    {
        var sink = new CollectingSink();
        var previousLogger = Serilog.Log.Logger;
        Serilog.Log.Logger = new Serilog.LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(sink).CreateLogger();
        try
        {
            await LoadInitial("Admin#1");

            SetupFailure();
            Advance(AdminRoster.Ttl);
            await _roster.GetSnapshotAsync();
            await Settle();
            Advance(AdminRoster.FailureRetryDelay);
            await _roster.GetSnapshotAsync();
            await Settle();

            SetupTags("Admin#1");
            Advance(AdminRoster.FailureRetryDelay);
            await _roster.GetSnapshotAsync();
            await Settle();

            SetupFailure();
            Advance(AdminRoster.Ttl);
            await _roster.GetSnapshotAsync();
            await Settle();

            var levels = sink.Events.Where(e => e.Exception is HttpRequestException).Select(e => e.Level).ToList();
            CollectionAssert.AreEqual(
                new[] { LogEventLevel.Warning, LogEventLevel.Debug, LogEventLevel.Warning },
                levels);
        }
        finally
        {
            Serilog.Log.Logger = previousLogger;
        }
    }

    private sealed class CollectingSink : Serilog.Core.ILogEventSink
    {
        private readonly List<LogEvent> _events = [];

        public List<LogEvent> Events
        {
            get { lock (_events) return [.. _events]; }
        }

        public void Emit(LogEvent logEvent) { lock (_events) _events.Add(logEvent); }
    }
}
