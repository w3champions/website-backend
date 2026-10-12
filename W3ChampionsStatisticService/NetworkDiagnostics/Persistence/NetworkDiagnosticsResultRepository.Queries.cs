using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3ChampionsStatisticService.NetworkDiagnostics.Results;

namespace W3ChampionsStatisticService.NetworkDiagnostics.Persistence;

public partial class NetworkDiagnosticsResultRepository
{
    public async Task<(List<NetworkDiagnosticMeasurement> Items, long Total)> ListMeasurementsAsync(
        FilterDefinition<NetworkDiagnosticMeasurement> filter, int page, int pageSize)
    {
        var total = await Measurements.CountDocumentsAsync(filter);
        var items = await Measurements.Find(filter).SortByDescending(x => x.SubmittedAt).Skip(page * pageSize).Limit(pageSize).ToListAsync();
        return (items, total);
    }

    public async Task<(List<string> Ids, bool Truncated)> MeasurementIdsAsync(FilterDefinition<NetworkDiagnosticObservation> filter, int limit)
    {
        // The server reads at most `limit` matching rows, newest first, before it groups. Disk use is allowed because
        // a filter without a fitting index sorts that many rows in memory.
        var groups = await Observations.Aggregate(new AggregateOptions { AllowDiskUse = true })
            .Match(filter)
            .SortByDescending(x => x.SubmittedAt)
            .Limit(limit)
            .Group(x => x.MeasurementId, g => new { MeasurementId = g.Key, Rows = g.LongCount() })
            .ToListAsync();
        return (groups.Select(g => g.MeasurementId).ToList(), groups.Sum(g => g.Rows) >= limit);
    }
}
