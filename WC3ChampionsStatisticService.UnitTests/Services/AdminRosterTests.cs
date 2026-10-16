using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using W3ChampionsStatisticService.Services;
using WC3ChampionsStatisticService.Tests.NetworkDiagnostics;

namespace WC3ChampionsStatisticService.Tests.Services;

[TestFixture]
public class AdminRosterTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private Mock<IdentityServiceClient> _identityClientMock;
    private FixedTimeProvider _clock;
    private AdminRoster _roster;

    [SetUp]
    public void SetUp()
    {
        _identityClientMock = new Mock<IdentityServiceClient>(new WC3ChampionsStatisticService.Tests.Admin.StubHttpClientFactory(new HttpClient()));
        _clock = new FixedTimeProvider(Start);
        _roster = new AdminRoster(_identityClientMock.Object, _clock);
    }

    private void SetupTags(params string[] tags) =>
        _identityClientMock.Setup(c => c.GetAdminBattleTags()).ReturnsAsync(tags.ToList());

    private void Advance(TimeSpan by) => _clock.Now += by;

    [Test]
    public async Task FirstCall_LoadsFromIdentityService()
    {
        SetupTags("Admin#1");

        Assert.IsTrue(await _roster.IsAdmin("Admin#1"));
        Assert.IsFalse(await _roster.IsAdmin("Other#2"));
    }

    [Test]
    public async Task IsAdmin_IsCaseInsensitive()
    {
        SetupTags("Admin#1");

        Assert.IsTrue(await _roster.IsAdmin("ADMIN#1"));
        var set = await _roster.GetAdminBattleTags();
        Assert.IsTrue(set.Contains("admin#1"));
    }

    [Test]
    public async Task WithinTtl_DoesNotRefetch()
    {
        SetupTags("Admin#1");

        await _roster.IsAdmin("Admin#1");
        Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
        await _roster.IsAdmin("Admin#1");
        await _roster.GetAdminBattleTags();

        _identityClientMock.Verify(c => c.GetAdminBattleTags(), Times.Once);
    }

    [Test]
    public async Task AfterTtl_RefetchesAndServesNewSet()
    {
        SetupTags("Admin#1");
        await _roster.IsAdmin("Admin#1");

        SetupTags("New#2");
        Advance(AdminRoster.Ttl);

        Assert.IsFalse(await _roster.IsAdmin("Admin#1"));
        Assert.IsTrue(await _roster.IsAdmin("New#2"));
    }

    [Test]
    public async Task RefreshFailure_ServesLastGoodSet()
    {
        SetupTags("Admin#1");
        await _roster.IsAdmin("Admin#1");

        _identityClientMock.Setup(c => c.GetAdminBattleTags()).ThrowsAsync(new HttpRequestException("boom"));
        Advance(AdminRoster.Ttl);

        Assert.IsTrue(await _roster.IsAdmin("Admin#1"));
    }

    [Test]
    public async Task RefreshFailure_RetriesOnlyAfterFailureRetryDelay()
    {
        SetupTags("Admin#1");
        await _roster.IsAdmin("Admin#1");

        _identityClientMock.Setup(c => c.GetAdminBattleTags()).ThrowsAsync(new HttpRequestException("boom"));
        Advance(AdminRoster.Ttl);
        await _roster.IsAdmin("Admin#1");
        _identityClientMock.Verify(c => c.GetAdminBattleTags(), Times.Exactly(2));

        Advance(TimeSpan.FromSeconds(10));
        await _roster.IsAdmin("Admin#1");
        _identityClientMock.Verify(c => c.GetAdminBattleTags(), Times.Exactly(2));

        Advance(AdminRoster.FailureRetryDelay - TimeSpan.FromSeconds(10));
        await _roster.IsAdmin("Admin#1");
        _identityClientMock.Verify(c => c.GetAdminBattleTags(), Times.Exactly(3));
    }

    [Test]
    public async Task NeverLoaded_FailureFailsOpenAsNoAdmins()
    {
        _identityClientMock.Setup(c => c.GetAdminBattleTags()).ThrowsAsync(new TaskCanceledException());

        Assert.IsFalse(await _roster.IsAdmin("Admin#1"));
        Assert.IsEmpty(await _roster.GetAdminBattleTags());
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    public async Task IsAdmin_BlankTag_FalseWithoutFetching(string tag)
    {
        Assert.IsFalse(await _roster.IsAdmin(tag));

        _identityClientMock.Verify(c => c.GetAdminBattleTags(), Times.Never);
    }

    [Test]
    public async Task ConcurrentFirstCalls_FetchOnce()
    {
        var gate = new TaskCompletionSource<List<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _identityClientMock.Setup(c => c.GetAdminBattleTags()).Returns(gate.Task);

        var calls = Enumerable.Range(0, 5).Select(_ => Task.Run(() => _roster.IsAdmin("Admin#1"))).ToList();
        gate.SetResult(new List<string> { "Admin#1" });
        var results = await Task.WhenAll(calls);

        Assert.IsTrue(results.All(r => r));
        _identityClientMock.Verify(c => c.GetAdminBattleTags(), Times.Once);
    }
}
