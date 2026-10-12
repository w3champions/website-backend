using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.NetworkDiagnostics.Api;

namespace W3ChampionsStatisticService.NetworkDiagnostics.Results;

public sealed partial class NetworkDiagnosticsQueryService
{
    public const int DefaultObservationLimit = 1000;
    public const int MaxObservationLimit = 5000;

    public async Task<NetDiagResult> GetObservationsAsync([NoTrace] NetDiagQuery query, string cursor, int limit, [NoTrace] CancellationToken cancellationToken = default)
    {
        var (range, invalid) = ObservationRange(query, cursor, limit);
        if (invalid != null) return invalid;

        // One row more than asked tells whether a next page exists.
        var rows = await results.ObservationsAsync(range.Filter, range.After, limit + 1, cancellationToken);
        var page = rows.Take(limit).ToList();
        var next = rows.Count > limit ? page[^1].Id.ToString() : null;
        return NetDiagResult.Ok(new ObservationPage(page.Select(ObservationDto.From).ToList(), next));
    }

    public async Task<NetDiagResult> OpenObservationStreamAsync([NoTrace] NetDiagQuery query, string cursor, int limit, [NoTrace] CancellationToken cancellationToken = default)
    {
        var (range, invalid) = ObservationRange(query, cursor, limit);
        if (invalid != null) return invalid;

        // The next cursor is a response header, so it must be known before the first row is written: the end of the
        // page is looked up first, and the stream is bounded by it. Rows that appear or expire between the two
        // queries can then only shorten the page, never move it past the cursor the client was given.
        var pageEnd = await results.ObservationPageEndAsync(range.Filter, range.After, limit, cancellationToken);
        var rows = await results.OpenObservationsAsync(range.Filter, range.After, pageEnd, limit, cancellationToken);
        return NetDiagResult.Ok(new ObservationStream(rows, pageEnd?.ToString()));
    }

    public async Task<NetDiagResult> AggregateAsync([NoTrace] NetDiagQuery query, string groupBy, [NoTrace] CancellationToken cancellationToken = default)
    {
        query ??= new NetDiagQuery();
        if (query.InvalidParameter() is { } key) return AdminRequests.InvalidParameter(key);
        var keys = AggregateBuilder.ParseGroupBy(groupBy);
        if (keys == null) return AdminRequests.InvalidParameter("groupBy");

        var filter = NetDiagFilters.Observations(query);
        var groups = await results.AggregateObservationsAsync(
            filter, AggregateBuilder.GroupStages(keys), NetDiagConstants.MaxScannedObservations, AggregateBuilder.MaxRows, cancellationToken);
        var rows = groups.Select(group => AggregateBuilder.Finalize(group, keys)).ToList();

        // P6: was the row limit reached? Every grouped row is counted in exactly one group, so the answered counts
        // say so, unless groups were cut off at MaxRows: then the matching rows are counted, up to the limit.
        var groupedRows = rows.Sum(row => (long)row.Count);
        var truncated = groupedRows >= NetDiagConstants.MaxScannedObservations
            || (rows.Count >= AggregateBuilder.MaxRows
                && await results.CountObservationsAsync(filter, NetDiagConstants.MaxScannedObservations, cancellationToken) >= NetDiagConstants.MaxScannedObservations);
        return NetDiagResult.Ok(new AggregateResponseDto(keys, rows)) with { Truncated = truncated };
    }

    /// <summary>The filter and the position a page starts after, or the 400 for a bad filter, limit or cursor.</summary>
    private static ((FilterDefinition<NetworkDiagnosticObservation> Filter, ObjectId? After) Range, NetDiagResult Invalid) ObservationRange(
        NetDiagQuery query, string cursor, int limit)
    {
        query ??= new NetDiagQuery();
        if (limit is < 1 or > MaxObservationLimit) return (default, NetDiagResult.Error(400, "limit must be in 1-5000"));
        if (query.InvalidParameter() is { } key) return (default, AdminRequests.InvalidParameter(key));
        ObjectId? after = null;
        if (!string.IsNullOrEmpty(cursor))
        {
            // The cursor is the _id of the last row of the previous page. Anything that is not an ObjectId is refused;
            // a well-formed one only ever narrows the filter.
            if (!ObjectId.TryParse(cursor, out var parsed)) return (default, AdminRequests.InvalidParameter("cursor"));
            after = parsed;
        }
        return ((NetDiagFilters.Observations(query), after), null);
    }
}
