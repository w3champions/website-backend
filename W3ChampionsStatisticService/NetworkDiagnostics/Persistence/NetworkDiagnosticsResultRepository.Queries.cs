using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.NetworkDiagnostics.Results;

namespace W3ChampionsStatisticService.NetworkDiagnostics.Persistence;

/// <summary>
/// Admin reads. Every one carries <see cref="NetDiagConstants.AdminQueryMaxTime"/>: limits bound what a query
/// returns, only the time limit bounds what the server examines for a filter that no index serves.
/// </summary>
public partial class NetworkDiagnosticsResultRepository
{
    private static FindOptions AdminFind => new() { MaxTime = NetDiagConstants.AdminQueryMaxTime };

    public async Task<(List<NetworkDiagnosticMeasurement> Items, long Total)> ListMeasurementsAsync(
        [NoTrace] FilterDefinition<NetworkDiagnosticMeasurement> filter, int page, int pageSize)
    {
        var total = await CountMeasurementsAsync(filter);
        var items = await Measurements.Find(filter, AdminFind).SortByDescending(x => x.SubmittedAt).Skip(page * pageSize).Limit(pageSize).ToListAsync();
        return (items, total);
    }

    /// <summary>
    /// Measurement documents embed every target, path, probe and hop, so counting them means reading them. The
    /// unfiltered list (the default view) takes the collection's size from its metadata; a filtered one stops
    /// counting at <see cref="NetDiagConstants.MaxCountedMeasurements"/>.
    /// </summary>
    private Task<long> CountMeasurementsAsync(FilterDefinition<NetworkDiagnosticMeasurement> filter)
    {
        var measurements = Measurements;
        var matchesEverything = filter.Render(new RenderArgs<NetworkDiagnosticMeasurement>(
            measurements.DocumentSerializer, measurements.Settings.SerializerRegistry)).ElementCount == 0;
        return matchesEverything
            ? measurements.EstimatedDocumentCountAsync(new EstimatedDocumentCountOptions { MaxTime = NetDiagConstants.AdminQueryMaxTime })
            : measurements.CountDocumentsAsync(filter, new CountOptions { Limit = NetDiagConstants.MaxCountedMeasurements, MaxTime = NetDiagConstants.AdminQueryMaxTime });
    }

    public async Task<(List<string> Ids, bool Truncated)> MeasurementIdsAsync([NoTrace] FilterDefinition<NetworkDiagnosticObservation> filter, int limit)
    {
        // At most `limit` matching rows, newest first, reach the $group. Disk use is allowed because a filter
        // without a fitting index sorts that many rows; the time limit ends a scan that finds too few matches.
        var groups = await Observations.Aggregate(new AggregateOptions { AllowDiskUse = true, MaxTime = NetDiagConstants.AdminQueryMaxTime })
            .Match(filter)
            .SortByDescending(x => x.SubmittedAt)
            .Limit(limit)
            .Group(x => x.MeasurementId, g => new { MeasurementId = g.Key, Rows = g.LongCount() })
            .ToListAsync();
        return (groups.Select(g => g.MeasurementId).ToList(), groups.Sum(g => g.Rows) >= limit);
    }

    /// <summary>Rows per round trip of a streamed observation read: what an export holds in memory at a time.</summary>
    private const int ObservationBatchSize = 500;

    public Task<List<NetworkDiagnosticObservation>> ObservationsAsync(
        [NoTrace] FilterDefinition<NetworkDiagnosticObservation> filter, int limit, [NoTrace] CancellationToken cancellationToken) =>
        // The whole page is kept in memory anyway, so it is fetched in one batch.
        Observations.Find(filter, new FindOptions { MaxTime = NetDiagConstants.AdminQueryMaxTime, BatchSize = limit })
            .SortBy(x => x.Id).Limit(limit).ToListAsync(cancellationToken);

    public async Task<ObjectId?> ObservationPageEndAsync(
        [NoTrace] FilterDefinition<NetworkDiagnosticObservation> filter, int limit, [NoTrace] CancellationToken cancellationToken)
    {
        // The last row of the page and the row after it.
        var ids = await Observations.Find(filter, AdminFind)
            .SortBy(x => x.Id).Skip(limit - 1).Limit(2).Project(x => x.Id).ToListAsync(cancellationToken);
        return ids.Count == 2 ? ids[0] : null;
    }

    public Task<IAsyncCursor<NetworkDiagnosticObservation>> OpenObservationsAsync(
        [NoTrace] FilterDefinition<NetworkDiagnosticObservation> filter, int? limit, [NoTrace] CancellationToken cancellationToken) =>
        Observations.Find(filter, new FindOptions { MaxTime = NetDiagConstants.AdminQueryMaxTime, BatchSize = ObservationBatchSize })
            .SortBy(x => x.Id).Limit(limit).ToCursorAsync(cancellationToken);

    public Task<List<BsonDocument>> AggregateObservationsAsync(
        [NoTrace] FilterDefinition<NetworkDiagnosticObservation> filter, [NoTrace] IReadOnlyList<BsonDocument> groupStages,
        int scanLimit, int maxRows, [NoTrace] CancellationToken cancellationToken)
    {
        // As in MeasurementIdsAsync: at most `scanLimit` matching rows, newest first, reach the groups (P6).
        var pipeline = Observations.Aggregate(new AggregateOptions { AllowDiskUse = true, MaxTime = NetDiagConstants.AdminQueryMaxTime })
            .Match(filter)
            .SortByDescending(x => x.SubmittedAt)
            .Limit(scanLimit)
            .As<BsonDocument>();
        foreach (var group in groupStages)
        {
            pipeline = pipeline.AppendStage<BsonDocument>(new BsonDocument("$group", group));
        }
        return pipeline
            .Sort(new BsonDocument { { "count", -1 }, { "_id", 1 } })
            .Limit(maxRows)
            .ToListAsync(cancellationToken);
    }

    public Task<long> CountObservationsAsync([NoTrace] FilterDefinition<NetworkDiagnosticObservation> filter, int limit, [NoTrace] CancellationToken cancellationToken) =>
        Observations.CountDocumentsAsync(filter, new CountOptions { Limit = limit, MaxTime = NetDiagConstants.AdminQueryMaxTime }, cancellationToken);
}
