using System;
using System.Linq;
using System.Threading.Tasks;
using Mongo2Go;
using MongoDB.Driver;
using NUnit.Framework;
using W3ChampionsStatisticService.NetworkDiagnostics.Persistence;
using W3ChampionsStatisticService.NetworkDiagnostics.Runs;

namespace WC3ChampionsStatisticService.Tests.NetworkDiagnostics;

[TestFixture]
public class NetworkDiagnosticsRunRepositoryMongoTests
{
    private static readonly DateTime Now = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);
    private MongoDbRunner _runner;
    private NetworkDiagnosticsRunRepository _repo;

    [SetUp]
    public async Task SetUp()
    {
        _runner = MongoDbRunner.Start();
        _repo = new NetworkDiagnosticsRunRepository(new MongoClient(_runner.ConnectionString));
        await _repo.EnsureIndexesAsync();
    }

    [TearDown]
    public void TearDown() => _runner.Dispose();

    private static NetworkDiagnosticRun Run(string id, string kind = AudienceKind.All, string by = "Admin#1", int minutesAgo = 0, DateTime? windowEnd = null) => new()
    {
        Id = id,
        CreatedAt = Now.AddMinutes(-minutesAgo),
        CreatedBy = by,
        Audience = new RunAudience { Kind = kind },
        Targets = new RunTargets(),
        WindowEnd = windowEnd,
        ExpiresAt = Now.AddDays(379),
    };

    [Test]
    public async Task TryCancel_OnlyOnce()
    {
        await _repo.InsertAsync(Run("r1"));

        var cancelled = await _repo.TryCancelAsync("r1", Now);
        Assert.AreEqual(Now, cancelled.CancelledAt);
        Assert.IsNull(await _repo.TryCancelAsync("r1", Now));
        Assert.IsNull(await _repo.TryCancelAsync("missing", Now));
    }

    [Test]
    public async Task IncrementCounters_AddsAtomically()
    {
        await _repo.InsertAsync(Run("r1"));
        await Task.WhenAll(_repo.IncrementCountersAsync("r1", delivered: 1), _repo.IncrementCountersAsync("r1", delivered: 1, submitted: 1));

        var c = (await _repo.GetAsync("r1")).Counters;
        Assert.AreEqual((0, 2, 1, 0), (c.Assigned, c.Delivered, c.Submitted, c.Expired));
    }

    [Test]
    public async Task List_FiltersPagesAndSortsNewestFirst()
    {
        await _repo.InsertAsync(Run("old", minutesAgo: 30));
        await _repo.InsertAsync(Run("new", minutesAgo: 1));
        await _repo.InsertAsync(Run("other", by: "Admin#2", minutesAgo: 2));

        var (items, total) = await _repo.ListAsync("Admin#1", null, null, 0, 1);
        Assert.AreEqual(2, total);
        Assert.AreEqual("new", items[0].Id);
        var (window, _) = await _repo.ListAsync(null, Now.AddMinutes(-5), Now, 0, 25);
        Assert.AreEqual(new[] { "new", "other" }, window.ConvertAll(r => r.Id));
    }

    // A5: page 0 is the first page; from is inclusive, to is exclusive.
    [Test]
    public async Task List_PageIsZeroBased_FromInclusive_ToExclusive()
    {
        await _repo.InsertAsync(Run("a", minutesAgo: 3));
        await _repo.InsertAsync(Run("b", minutesAgo: 2));
        await _repo.InsertAsync(Run("c", minutesAgo: 1));

        var (second, total) = await _repo.ListAsync(null, null, null, 1, 1);
        Assert.AreEqual(3, total);
        Assert.AreEqual("b", second.Single().Id, "page 1 with pageSize 1 is the second-newest run");

        var (bounded, _) = await _repo.ListAsync(null, Now.AddMinutes(-3), Now.AddMinutes(-1), 0, 25);
        Assert.AreEqual(new[] { "b", "a" }, bounded.ConvertAll(r => r.Id), "a (== from) included, c (== to) excluded");
    }

    [Test]
    public async Task ActiveFleetRuns_ExcludesUsersCancelledAndClosed()
    {
        await _repo.InsertAsync(Run("open", windowEnd: Now.AddMinutes(10)));
        await _repo.InsertAsync(Run("closed", windowEnd: Now.AddMinutes(-1)));
        await _repo.InsertAsync(Run("users", kind: AudienceKind.Users, windowEnd: null));
        await _repo.InsertAsync(Run("cancelled", windowEnd: Now.AddMinutes(10)));
        await _repo.TryCancelAsync("cancelled", Now);

        Assert.AreEqual(new[] { "open" }, (await _repo.ActiveFleetRunsAsync(Now)).ConvertAll(r => r.Id));
    }
}
