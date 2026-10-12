using System;
using System.Linq;
using System.Threading.Tasks;
using Mongo2Go;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using W3ChampionsStatisticService.NetworkDiagnostics;
using W3ChampionsStatisticService.NetworkDiagnostics.Persistence;
using W3ChampionsStatisticService.NetworkDiagnostics.Runs;

namespace WC3ChampionsStatisticService.Tests.NetworkDiagnostics;

[TestFixture]
public class NetworkDiagnosticsAssignmentRepositoryMongoTests
{
    private static readonly DateTime Now = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);
    private MongoDbRunner _runner;
    private MongoClient _client;
    private NetworkDiagnosticsAssignmentRepository _repo;

    [SetUp]
    public async Task SetUp()
    {
        _runner = MongoDbRunner.Start();
        _client = new MongoClient(_runner.ConnectionString);
        _repo = new NetworkDiagnosticsAssignmentRepository(_client);
        await _repo.EnsureIndexesAsync();
    }

    [TearDown]
    public void TearDown() => _runner.Dispose();

    private static NetworkDiagnosticAssignment New(string runId = "run-1", string tag = "Name#1234", DateTime? after = null, DateTime? by = null) =>
        AssignmentFactory.Create(runId, tag, after ?? Now.AddMinutes(-1), by ?? Now.AddDays(1));

    [Test]
    public async Task EnsureIndexes_CreatesTtlAndUniqueRunTag()
    {
        var indexes = await (await _client.GetDatabase("W3Champions-Statistic-Service")
            .GetCollection<BsonDocument>(NetDiagConstants.AssignmentsCollection).Indexes.ListAsync()).ToListAsync();

        Assert.IsTrue(indexes.Any(i => i["key"].AsBsonDocument.Contains("ExpiresAt") && i.GetValue("expireAfterSeconds", -1).ToInt64() == 0));
        Assert.IsTrue(indexes.Any(i => i["key"].AsBsonDocument.Names.SequenceEqual(["RunId", "BattleTag"]) && i.GetValue("unique", false).ToBoolean()));
    }

    [Test]
    public async Task TrySubmit_SecondCallFails()
    {
        var a = New();
        await _repo.InsertManyAsync([a]);

        var first = await _repo.TrySubmitAsync(a.Id, a.BattleTag, Now);
        Assert.IsNull(await _repo.TrySubmitAsync(a.Id, a.BattleTag, Now));

        Assert.AreEqual(AssignmentStatus.Submitted, first.Status);
        Assert.AreEqual("run-1", first.RunId);
        var stored = await _repo.GetAsync(a.Id);
        Assert.AreEqual(AssignmentStatus.Submitted, stored.Status);
        Assert.AreEqual(Now, stored.SubmittedAt);
    }

    [Test]
    public async Task TrySubmit_RejectsForeignTagAndPastDeliverBy()
    {
        var a = New(by: Now.AddSeconds(-1));
        var b = New(runId: "run-2");
        await _repo.InsertManyAsync([a, b]);

        Assert.IsNull(await _repo.TrySubmitAsync(a.Id, a.BattleTag, Now), "past deliverBy");
        Assert.IsNull(await _repo.TrySubmitAsync(b.Id, "Other#1111", Now), "foreign tag");
        Assert.AreEqual(AssignmentStatus.Pending, (await _repo.GetAsync(b.Id)).Status);
    }

    // A10: the plaintext token round-trips through Mongo, so every re-push sends the same value.
    [Test]
    public async Task Token_IsStoredAndReadBackUnchanged()
    {
        var a = New();
        await _repo.InsertManyAsync([a]);

        Assert.AreEqual(a.Token, (await _repo.GetAsync(a.Id)).Token);
    }

    [Test]
    public async Task TryMarkDelivered_OnlyTheFirstCallWins()
    {
        var a = New();
        await _repo.InsertManyAsync([a]);

        Assert.IsTrue(await _repo.TryMarkDeliveredAsync(a.Id, Now));
        Assert.IsFalse(await _repo.TryMarkDeliveredAsync(a.Id, Now.AddSeconds(5)));
        Assert.AreEqual(Now, (await _repo.GetAsync(a.Id)).DeliveredAt);
    }

    [Test]
    public async Task TryInsert_DuplicateRunAndTag_ReturnsFalse()
    {
        Assert.IsTrue(await _repo.TryInsertAsync(New()));
        Assert.IsFalse(await _repo.TryInsertAsync(New()));
        Assert.IsTrue(await _repo.ExistsAsync("run-1", "Name#1234"));
        Assert.AreEqual(new[] { "Name#1234" }, await _repo.AssignedBattleTagsAsync("run-1"));
    }

    [Test]
    public async Task Due_OnlyPendingInsideTheDeliveryWindow()
    {
        var due = New(tag: "A#1111");
        var early = New(tag: "B#2222", after: Now.AddMinutes(1));
        var late = New(tag: "C#3333", by: Now.AddMinutes(-1));
        var delivered = New(tag: "D#4444");
        await _repo.InsertManyAsync([due, early, late, delivered]);
        await _repo.TryMarkDeliveredAsync(delivered.Id, Now);

        Assert.AreEqual(new[] { due.Id }, (await _repo.DueForAsync("A#1111", Now)).Select(x => x.Id));
        Assert.IsEmpty(await _repo.DueForAsync("B#2222", Now));
        Assert.AreEqual(1, (await _repo.DueForAsync("D#4444", Now)).Count, "delivered but pending is re-pushed on reconnect");

        var undelivered = await _repo.DueUndeliveredAsync(["A#1111", "B#2222", "C#3333", "D#4444"], Now, 500);
        Assert.AreEqual(new[] { due.Id }, undelivered.Select(x => x.Id));
        Assert.AreEqual(2, await _repo.CountPendingUndeliveredAsync("run-1", ["A#1111", "B#2222", "C#3333", "D#4444"], Now), "A due + B scheduled");
    }

    [Test]
    public async Task ExpireOverdue_CountsPerRun_AndLeavesSubmittedAlone()
    {
        var r1a = New("run-1", "A#1111", by: Now.AddMinutes(-1));
        var r1b = New("run-1", "B#2222", by: Now.AddMinutes(-1));
        var r2a = New("run-2", "A#1111", by: Now.AddMinutes(-1));
        var open = New("run-2", "C#3333");
        await _repo.InsertManyAsync([r1a, r1b, r2a, open]);
        // Simulate a submit that landed before the deadline.
        await _client.GetDatabase("W3Champions-Statistic-Service").GetCollection<NetworkDiagnosticAssignment>(NetDiagConstants.AssignmentsCollection)
            .UpdateOneAsync(x => x.Id == r1b.Id, Builders<NetworkDiagnosticAssignment>.Update.Set(x => x.Status, AssignmentStatus.Submitted));

        var expired = await _repo.ExpireOverdueAsync(Now);

        Assert.AreEqual(1, expired["run-1"]);
        Assert.AreEqual(1, expired["run-2"]);
        Assert.AreEqual(AssignmentStatus.Submitted, (await _repo.GetAsync(r1b.Id)).Status);
        Assert.AreEqual(AssignmentStatus.Pending, (await _repo.GetAsync(open.Id)).Status);
        Assert.IsEmpty(await _repo.ExpireOverdueAsync(Now));
        Assert.AreEqual(1, await _repo.ExpireAllPendingAsync("run-2"));
    }
}
