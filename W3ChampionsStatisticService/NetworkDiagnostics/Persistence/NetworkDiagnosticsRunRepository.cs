using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3C.Domain.Repositories;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.NetworkDiagnostics.Runs;

namespace W3ChampionsStatisticService.NetworkDiagnostics.Persistence;

public interface INetworkDiagnosticsRunRepository
{
    Task InsertAsync(NetworkDiagnosticRun run);
    Task<NetworkDiagnosticRun> GetAsync(string id);
    Task<(List<NetworkDiagnosticRun> Items, long Total)> ListAsync(string createdBy, DateTime? from, DateTime? to, int page, int pageSize);
    Task<List<NetworkDiagnosticRun>> ActiveFleetRunsAsync(DateTime now);
    Task<NetworkDiagnosticRun> TryCancelAsync(string id, DateTime now);
    Task IncrementCountersAsync(string id, int assigned = 0, int delivered = 0, int submitted = 0, int expired = 0);
}

[Trace]
public class NetworkDiagnosticsRunRepository(MongoClient mongoClient)
    : MongoDbRepositoryBase(mongoClient), INetworkDiagnosticsRunRepository, IRequiresIndexes
{
    public string CollectionName => NetDiagConstants.RunsCollection;

    private static readonly FilterDefinitionBuilder<NetworkDiagnosticRun> F = Builders<NetworkDiagnosticRun>.Filter;
    private static readonly string[] FleetKinds = [AudienceKind.SampleCount, AudienceKind.SamplePercent, AudienceKind.All];

    private IMongoCollection<NetworkDiagnosticRun> Collection => CreateCollection<NetworkDiagnosticRun>(CollectionName);

    public Task InsertAsync(NetworkDiagnosticRun run) => Collection.InsertOneAsync(run);

    public Task<NetworkDiagnosticRun> GetAsync(string id) => Collection.Find(F.Eq(x => x.Id, id)).FirstOrDefaultAsync();

    public async Task<(List<NetworkDiagnosticRun> Items, long Total)> ListAsync(string createdBy, DateTime? from, DateTime? to, int page, int pageSize)
    {
        var filter = F.Empty;
        if (!string.IsNullOrEmpty(createdBy)) filter &= F.Eq(x => x.CreatedBy, createdBy);
        if (from is { } f) filter &= F.Gte(x => x.CreatedAt, f);
        if (to is { } t) filter &= F.Lt(x => x.CreatedAt, t);
        var total = await Collection.CountDocumentsAsync(filter);
        var items = await Collection.Find(filter).SortByDescending(x => x.CreatedAt).Skip(page * pageSize).Limit(pageSize).ToListAsync();
        return (items, total);
    }

    public Task<List<NetworkDiagnosticRun>> ActiveFleetRunsAsync(DateTime now) =>
        Collection.Find(F.In(x => x.Audience.Kind, FleetKinds) & F.Eq(x => x.CancelledAt, null) & F.Gt(x => x.WindowEnd, now)).ToListAsync();

    public Task<NetworkDiagnosticRun> TryCancelAsync(string id, DateTime now) =>
        Collection.FindOneAndUpdateAsync(
            F.Eq(x => x.Id, id) & F.Eq(x => x.CancelledAt, null),
            Builders<NetworkDiagnosticRun>.Update.Set(x => x.CancelledAt, now),
            new FindOneAndUpdateOptions<NetworkDiagnosticRun> { ReturnDocument = ReturnDocument.After });

    public Task IncrementCountersAsync(string id, int assigned = 0, int delivered = 0, int submitted = 0, int expired = 0) =>
        Collection.UpdateOneAsync(
            F.Eq(x => x.Id, id),
            Builders<NetworkDiagnosticRun>.Update
                .Inc(x => x.Counters.Assigned, assigned)
                .Inc(x => x.Counters.Delivered, delivered)
                .Inc(x => x.Counters.Submitted, submitted)
                .Inc(x => x.Counters.Expired, expired));

    public Task EnsureIndexesAsync()
    {
        var k = Builders<NetworkDiagnosticRun>.IndexKeys;
        return Collection.Indexes.CreateManyAsync(
        [
            new(k.Ascending(x => x.ExpiresAt), new CreateIndexOptions { ExpireAfter = TimeSpan.Zero }),
            new(k.Descending(x => x.CreatedAt)),
            new(k.Ascending(x => x.CreatedBy).Descending(x => x.CreatedAt)),
            new(k.Ascending(x => x.WindowEnd).Ascending(x => x.CancelledAt)),
        ]);
    }
}
