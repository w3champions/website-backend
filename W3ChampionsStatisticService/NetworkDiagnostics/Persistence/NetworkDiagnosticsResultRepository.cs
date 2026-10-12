using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using W3C.Domain.Repositories;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.NetworkDiagnostics.Results;
using W3ChampionsStatisticService.NetworkDiagnostics.Submission;

namespace W3ChampionsStatisticService.NetworkDiagnostics.Persistence;

public interface INetworkDiagnosticsResultRepository
{
    /// <summary>The result carries the client IP and geo, so it stays out of traces (P13). The attribute sits on the interface because the tracing proxy reads it there.</summary>
    Task InsertResultAsync([NoTrace] BuiltResult result);
    Task UpsertIpInfoAsync(IReadOnlyCollection<NetworkDiagnosticIpInfo> rows);
    Task<NetworkDiagnosticMeasurement> GetMeasurementAsync(string id);
    Task<NetworkDiagnosticRawSeries> GetRawSeriesAsync(string id);
    Task<List<NetworkDiagnosticIpInfo>> IpInfoAsync(string runId, IReadOnlyCollection<string> ips);
    Task<Dictionary<string, ClientGeo>> ClientGeoByMeasurementAsync(IReadOnlyCollection<string> measurementIds);
    /// <summary>
    /// Newest first; skips page * pageSize (zero-based page, A5). Total is the collection's estimated size for an
    /// empty filter and otherwise the number of matches, counted up to <see cref="NetDiagConstants.MaxCountedMeasurements"/>.
    /// The filter carries a player's BattleTag, so it stays out of traces.
    /// </summary>
    Task<(List<NetworkDiagnosticMeasurement> Items, long Total)> ListMeasurementsAsync([NoTrace] FilterDefinition<NetworkDiagnosticMeasurement> filter, int page, int pageSize);
    /// <summary>
    /// The measurements of the newest <paramref name="limit"/> observations matching the filter. Truncated is true
    /// when that many rows matched, so older matches may be missing. The limit bounds the rows that MATCH, not the
    /// rows the server examines to find them: a filter no index serves examines rows until the server-side time
    /// limit (<see cref="NetDiagConstants.AdminQueryMaxTime"/>) ends the query with a MongoExecutionTimeoutException.
    /// </summary>
    Task<(List<string> Ids, bool Truncated)> MeasurementIdsAsync([NoTrace] FilterDefinition<NetworkDiagnosticObservation> filter, int limit);
    /// <summary>
    /// The first <paramref name="limit"/> matching rows in _id order, read in one round trip. The caller puts the
    /// _id range of a page into the filter (<see cref="NetDiagFilters.IdRange"/>).
    /// </summary>
    Task<List<NetworkDiagnosticObservation>> ObservationsAsync(
        [NoTrace] FilterDefinition<NetworkDiagnosticObservation> filter, int limit, [NoTrace] CancellationToken cancellationToken);
    /// <summary>
    /// The _id of the <paramref name="limit"/>-th matching row in _id order when at least one more matching row
    /// follows it, else null: the limit does not end a page of that size.
    /// </summary>
    Task<ObjectId?> ObservationPageEndAsync(
        [NoTrace] FilterDefinition<NetworkDiagnosticObservation> filter, int limit, [NoTrace] CancellationToken cancellationToken);
    /// <summary>
    /// The matching rows in _id order as an open server cursor (its first batch is already read), read in batches.
    /// A null <paramref name="limit"/> returns every match: the filter's _id range is then the only bound. The
    /// caller disposes the cursor.
    /// </summary>
    Task<IAsyncCursor<NetworkDiagnosticObservation>> OpenObservationsAsync(
        [NoTrace] FilterDefinition<NetworkDiagnosticObservation> filter, int? limit, [NoTrace] CancellationToken cancellationToken);
    /// <summary>
    /// Runs the <c>$group</c> stages over the newest <paramref name="scanLimit"/> matching rows and returns at most
    /// <paramref name="maxRows"/> final groups, largest "count" first.
    /// </summary>
    Task<List<BsonDocument>> AggregateObservationsAsync(
        [NoTrace] FilterDefinition<NetworkDiagnosticObservation> filter, [NoTrace] IReadOnlyList<BsonDocument> groupStages,
        int scanLimit, int maxRows, [NoTrace] CancellationToken cancellationToken);
    /// <summary>The number of matching rows; counting stops at <paramref name="limit"/>.</summary>
    Task<long> CountObservationsAsync([NoTrace] FilterDefinition<NetworkDiagnosticObservation> filter, int limit, [NoTrace] CancellationToken cancellationToken);
}

[Trace]
public partial class NetworkDiagnosticsResultRepository(MongoClient mongoClient)
    : MongoDbRepositoryBase(mongoClient), INetworkDiagnosticsResultRepository, IRequiresIndexes
{
    public string CollectionName => NetDiagConstants.MeasurementsCollection;

    private IMongoCollection<NetworkDiagnosticMeasurement> Measurements => CreateCollection<NetworkDiagnosticMeasurement>(NetDiagConstants.MeasurementsCollection);
    private IMongoCollection<NetworkDiagnosticRawSeries> RawSeries => CreateCollection<NetworkDiagnosticRawSeries>(NetDiagConstants.RawSeriesCollection);
    private IMongoCollection<NetworkDiagnosticIpInfo> IpInfo => CreateCollection<NetworkDiagnosticIpInfo>(NetDiagConstants.IpInfoCollection);
    private IMongoCollection<NetworkDiagnosticObservation> Observations => CreateCollection<NetworkDiagnosticObservation>(NetDiagConstants.ObservationsCollection);

    /// <summary>Measurement first (its _id = measurementId makes a replay fail loudly), then raw, then observations.</summary>
    public async Task InsertResultAsync([NoTrace] BuiltResult result)
    {
        await Measurements.InsertOneAsync(result.Measurement);
        await RawSeries.InsertOneAsync(result.RawSeries);
        if (result.Observations.Count > 0)
        {
            await Observations.InsertManyAsync(result.Observations, new InsertManyOptions { IsOrdered = false });
        }
    }

    public async Task UpsertIpInfoAsync(IReadOnlyCollection<NetworkDiagnosticIpInfo> rows)
    {
        if (rows.Count == 0) return;
        var writes = rows.Select(r => new UpdateOneModel<NetworkDiagnosticIpInfo>(
            Builders<NetworkDiagnosticIpInfo>.Filter.Eq(x => x.RunId, r.RunId) & Builders<NetworkDiagnosticIpInfo>.Filter.Eq(x => x.Ip, r.Ip),
            Builders<NetworkDiagnosticIpInfo>.Update
                .SetOnInsert(x => x.Id, r.Id)
                .SetOnInsert(x => x.Asn, r.Asn)
                .SetOnInsert(x => x.AsnOrg, r.AsnOrg)
                .SetOnInsert(x => x.Country, r.Country)
                .SetOnInsert(x => x.City, r.City)
                .SetOnInsert(x => x.Lat, r.Lat)
                .SetOnInsert(x => x.Lon, r.Lon)
                .SetOnInsert(x => x.GeoDbBuild, r.GeoDbBuild)
                .SetOnInsert(x => x.FirstSeenAt, r.FirstSeenAt)
                .SetOnInsert(x => x.ExpiresAt, r.ExpiresAt))
        { IsUpsert = true });
        try
        {
            await IpInfo.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = false });
        }
        catch (MongoBulkWriteException e) when (e.WriteErrors.All(w => w.Category == ServerErrorCategory.DuplicateKey))
        {
            // Two concurrent submits upserting the same (RunId, Ip): the loser's row already exists, which is the goal.
        }
    }

    public Task<NetworkDiagnosticMeasurement> GetMeasurementAsync(string id) =>
        Measurements.Find(Builders<NetworkDiagnosticMeasurement>.Filter.Eq(x => x.Id, id), AdminFind).FirstOrDefaultAsync();

    public Task<NetworkDiagnosticRawSeries> GetRawSeriesAsync(string id) =>
        RawSeries.Find(Builders<NetworkDiagnosticRawSeries>.Filter.Eq(x => x.Id, id), AdminFind).FirstOrDefaultAsync();

    public Task<List<NetworkDiagnosticIpInfo>> IpInfoAsync(string runId, IReadOnlyCollection<string> ips) =>
        IpInfo.Find(Builders<NetworkDiagnosticIpInfo>.Filter.Eq(x => x.RunId, runId) & Builders<NetworkDiagnosticIpInfo>.Filter.In(x => x.Ip, ips), AdminFind).ToListAsync();

    public async Task<Dictionary<string, ClientGeo>> ClientGeoByMeasurementAsync(IReadOnlyCollection<string> measurementIds)
    {
        var rows = await Measurements.Find(Builders<NetworkDiagnosticMeasurement>.Filter.In(x => x.Id, measurementIds), AdminFind)
            .Project(x => new { x.Id, x.ClientGeo }).ToListAsync();
        return rows.Where(r => r.ClientGeo != null).ToDictionary(r => r.Id, r => r.ClientGeo);
    }

    public async Task EnsureIndexesAsync()
    {
        var ttl = new CreateIndexOptions { ExpireAfter = TimeSpan.Zero };

        var m = Builders<NetworkDiagnosticMeasurement>.IndexKeys;
        await Measurements.Indexes.CreateManyAsync(
        [
            new(m.Ascending(x => x.ExpiresAt), ttl),
            new(m.Ascending(x => x.RunId).Descending(x => x.SubmittedAt)),
            new(m.Ascending(x => x.BattleTag).Descending(x => x.SubmittedAt)),
            new(m.Descending(x => x.SubmittedAt)),
            new(m.Ascending(x => x.ClientGeo.Country)),
            new(m.Ascending(x => x.ClientGeo.Asn)),
        ]);

        await RawSeries.Indexes.CreateOneAsync(new CreateIndexModel<NetworkDiagnosticRawSeries>(
            Builders<NetworkDiagnosticRawSeries>.IndexKeys.Ascending(x => x.ExpiresAt), ttl));

        var i = Builders<NetworkDiagnosticIpInfo>.IndexKeys;
        await IpInfo.Indexes.CreateManyAsync(
        [
            new(i.Ascending(x => x.ExpiresAt), ttl),
            new(i.Ascending(x => x.RunId).Ascending(x => x.Ip), new CreateIndexOptions { Unique = true }),
        ]);

        var o = Builders<NetworkDiagnosticObservation>.IndexKeys;
        await Observations.Indexes.CreateManyAsync(
        [
            new(o.Ascending(x => x.ExpiresAt), ttl),
            new(o.Ascending(x => x.MeasurementId)),
            new(o.Ascending(x => x.RunId).Ascending(x => x.TargetKey)),
            new(o.Ascending(x => x.TargetKey).Descending(x => x.SubmittedAt)),
            new(o.Ascending(x => x.Asn).Ascending(x => x.TargetKey)),
            new(o.Ascending(x => x.Country).Ascending(x => x.TargetKey)),
            new(o.Ascending(x => x.BattleTag).Descending(x => x.SubmittedAt)),
            new(o.Descending(x => x.SubmittedAt)),
            // The targetIp filter of the admin queries, newest first.
            new(o.Ascending(x => x.TargetIp).Descending(x => x.SubmittedAt)),
            // Observation pages are read in _id order; the usual export asks for the rows of some runs.
            new(o.Ascending(x => x.RunId).Ascending(x => x.Id)),
            // Spec section 4.6 (P14).
            new(o.Ascending(x => x.TargetKey).Ascending(x => x.Path).Ascending(x => x.Method).Ascending(x => x.SubmittedAt)),
        ]);
    }
}
