using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3C.Domain.Repositories;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.NetworkDiagnostics.Runs;

namespace W3ChampionsStatisticService.NetworkDiagnostics.Persistence;

public interface INetworkDiagnosticsAssignmentRepository
{
    Task InsertManyAsync(IReadOnlyCollection<NetworkDiagnosticAssignment> assignments);
    Task<bool> TryInsertAsync(NetworkDiagnosticAssignment assignment);
    Task<bool> ExistsAsync(string runId, string battleTag);
    Task<NetworkDiagnosticAssignment> GetAsync(string id);
    Task<List<NetworkDiagnosticAssignment>> ForRunAsync(string runId);
    Task<List<string>> AssignedBattleTagsAsync(string runId);
    Task<List<NetworkDiagnosticAssignment>> DueForAsync(string battleTag, DateTime now);
    Task<List<NetworkDiagnosticAssignment>> DueUndeliveredAsync(IReadOnlyCollection<string> battleTags, DateTime now, int limit);
    Task<int> CountPendingUndeliveredAsync(string runId, IReadOnlyCollection<string> battleTags, DateTime now);
    Task<bool> TryMarkDeliveredAsync(string id, DateTime now);
    Task<NetworkDiagnosticAssignment> TrySubmitAsync(string id, string battleTag, DateTime now);
    Task<Dictionary<string, int>> ExpireOverdueAsync(DateTime now);
    Task<int> ExpireAllPendingAsync(string runId);
    /// <summary>
    /// Expires the run's Pending assignments that were never delivered and returns how many. Delivered ones stay
    /// Pending: their launcher may still submit until DeliverBy.
    /// </summary>
    Task<int> ExpireUndeliveredAsync(string runId);
}

[Trace]
public class NetworkDiagnosticsAssignmentRepository(MongoClient mongoClient)
    : MongoDbRepositoryBase(mongoClient), INetworkDiagnosticsAssignmentRepository, IRequiresIndexes
{
    public string CollectionName => NetDiagConstants.AssignmentsCollection;

    private static readonly FilterDefinitionBuilder<NetworkDiagnosticAssignment> F = Builders<NetworkDiagnosticAssignment>.Filter;
    private static readonly UpdateDefinitionBuilder<NetworkDiagnosticAssignment> U = Builders<NetworkDiagnosticAssignment>.Update;

    private IMongoCollection<NetworkDiagnosticAssignment> Collection => CreateCollection<NetworkDiagnosticAssignment>(CollectionName);

    private static FilterDefinition<NetworkDiagnosticAssignment> PendingIn(DateTime now) =>
        F.Eq(x => x.Status, AssignmentStatus.Pending) & F.Lte(x => x.DeliverAfter, now) & F.Gt(x => x.DeliverBy, now);

    public async Task InsertManyAsync(IReadOnlyCollection<NetworkDiagnosticAssignment> assignments)
    {
        if (assignments.Count == 0) return;
        await Collection.InsertManyAsync(assignments, new InsertManyOptions { IsOrdered = false });
    }

    public async Task<bool> TryInsertAsync(NetworkDiagnosticAssignment assignment)
    {
        try
        {
            await Collection.InsertOneAsync(assignment);
            return true;
        }
        catch (MongoWriteException e) when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public async Task<bool> ExistsAsync(string runId, string battleTag) =>
        await Collection.Find(F.Eq(x => x.RunId, runId) & F.Eq(x => x.BattleTag, battleTag)).Limit(1).AnyAsync();

    public Task<NetworkDiagnosticAssignment> GetAsync(string id) => Collection.Find(F.Eq(x => x.Id, id)).FirstOrDefaultAsync();

    public Task<List<NetworkDiagnosticAssignment>> ForRunAsync(string runId) =>
        Collection.Find(F.Eq(x => x.RunId, runId)).SortBy(x => x.DeliverAfter).ToListAsync();

    public async Task<List<string>> AssignedBattleTagsAsync(string runId) =>
        await (await Collection.DistinctAsync(x => x.BattleTag, F.Eq(x => x.RunId, runId))).ToListAsync();

    public Task<List<NetworkDiagnosticAssignment>> DueForAsync(string battleTag, DateTime now) =>
        Collection.Find(F.Eq(x => x.BattleTag, battleTag) & PendingIn(now)).ToListAsync();

    public Task<List<NetworkDiagnosticAssignment>> DueUndeliveredAsync(IReadOnlyCollection<string> battleTags, DateTime now, int limit) =>
        Collection.Find(F.In(x => x.BattleTag, battleTags) & F.Eq(x => x.DeliveredAt, null) & PendingIn(now))
            .SortBy(x => x.DeliverAfter).Limit(limit).ToListAsync();

    public async Task<int> CountPendingUndeliveredAsync(string runId, IReadOnlyCollection<string> battleTags, DateTime now) =>
        (int)await Collection.CountDocumentsAsync(
            F.Eq(x => x.RunId, runId) & F.In(x => x.BattleTag, battleTags) & F.Eq(x => x.Status, AssignmentStatus.Pending)
            & F.Eq(x => x.DeliveredAt, null) & F.Gt(x => x.DeliverBy, now));

    public async Task<bool> TryMarkDeliveredAsync(string id, DateTime now) =>
        (await Collection.UpdateOneAsync(F.Eq(x => x.Id, id) & F.Eq(x => x.DeliveredAt, null), U.Set(x => x.DeliveredAt, now))).ModifiedCount == 1;

    /// <summary>
    /// The exactly-once gate (spec §5.5 step 2): one conditional update, so a retry can never write twice.
    /// Returns the updated assignment, or null when any precondition failed. The caller has already compared
    /// the token in constant time (A10); the token is immutable, so it is not part of this filter.
    /// </summary>
    public Task<NetworkDiagnosticAssignment> TrySubmitAsync(string id, string battleTag, DateTime now) =>
        Collection.FindOneAndUpdateAsync(
            F.Eq(x => x.Id, id) & F.Eq(x => x.BattleTag, battleTag)
            & F.Eq(x => x.Status, AssignmentStatus.Pending) & F.Gt(x => x.DeliverBy, now),
            U.Set(x => x.Status, AssignmentStatus.Submitted).Set(x => x.SubmittedAt, now),
            new FindOneAndUpdateOptions<NetworkDiagnosticAssignment> { ReturnDocument = ReturnDocument.After });

    public async Task<Dictionary<string, int>> ExpireOverdueAsync(DateTime now)
    {
        var overdue = F.Eq(x => x.Status, AssignmentStatus.Pending) & F.Lte(x => x.DeliverBy, now);
        var runIds = await (await Collection.DistinctAsync(x => x.RunId, overdue)).ToListAsync();
        var result = new Dictionary<string, int>();
        foreach (var runId in runIds)
        {
            var update = await Collection.UpdateManyAsync(overdue & F.Eq(x => x.RunId, runId), U.Set(x => x.Status, AssignmentStatus.Expired));
            if (update.ModifiedCount > 0) result[runId] = (int)update.ModifiedCount;
        }
        return result;
    }

    public async Task<int> ExpireAllPendingAsync(string runId) =>
        (int)(await Collection.UpdateManyAsync(
            F.Eq(x => x.RunId, runId) & F.Eq(x => x.Status, AssignmentStatus.Pending),
            U.Set(x => x.Status, AssignmentStatus.Expired))).ModifiedCount;

    public async Task<int> ExpireUndeliveredAsync(string runId) =>
        (int)(await Collection.UpdateManyAsync(
            F.Eq(x => x.RunId, runId) & F.Eq(x => x.Status, AssignmentStatus.Pending) & F.Eq(x => x.DeliveredAt, null),
            U.Set(x => x.Status, AssignmentStatus.Expired))).ModifiedCount;

    public Task EnsureIndexesAsync()
    {
        var k = Builders<NetworkDiagnosticAssignment>.IndexKeys;
        return Collection.Indexes.CreateManyAsync(
        [
            new(k.Ascending(x => x.ExpiresAt), new CreateIndexOptions { ExpireAfter = TimeSpan.Zero }),
            new(k.Ascending(x => x.RunId).Ascending(x => x.BattleTag), new CreateIndexOptions { Unique = true }),
            new(k.Ascending(x => x.BattleTag).Ascending(x => x.Status).Ascending(x => x.DeliverAfter)),
            new(k.Ascending(x => x.Status).Ascending(x => x.DeliveredAt).Ascending(x => x.DeliverAfter)),
            new(k.Ascending(x => x.Status).Ascending(x => x.DeliverBy)),
        ]);
    }
}
