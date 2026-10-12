using System;
using System.Buffers.Binary;
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

    /// <summary>
    /// The smallest _id of the second one settle margin before <paramref name="now"/>. Pages end before it: see
    /// <see cref="NetDiagConstants.ObservationSettleMargin"/>.
    /// </summary>
    public static ObjectId SettledBoundary(DateTimeOffset now)
    {
        // An ObjectId starts with its creation time as big-endian seconds; the other eight bytes stay zero.
        var bytes = new byte[ObjectIdBytes];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)(now - NetDiagConstants.ObservationSettleMargin).ToUnixTimeSeconds());
        return new ObjectId(bytes);
    }

    private const int ObjectIdBytes = 12;

    public async Task<NetDiagResult> GetObservationsAsync([NoTrace] NetDiagQuery query, string cursor, int limit, [NoTrace] CancellationToken cancellationToken = default)
    {
        var (range, invalid) = ObservationRange(query, cursor, limit);
        if (invalid != null) return invalid;

        // One row more than asked tells whether the limit ended the page. Only then is there a next cursor: a page
        // ended by the settle boundary is complete as of now minus the margin, and a cursor would send the client
        // polling for rows that are not settled yet.
        var rows = await results.ObservationsAsync(range.Rows(), limit + 1, cancellationToken);
        var page = rows.Take(limit).ToList();
        var next = rows.Count > limit ? page[^1].Id.ToString() : null;
        return NetDiagResult.Ok(new ObservationPage(page.Select(ObservationDto.From).ToList(), next));
    }

    public async Task<NetDiagResult> OpenObservationStreamAsync([NoTrace] NetDiagQuery query, string cursor, int limit, [NoTrace] CancellationToken cancellationToken = default)
    {
        var (range, invalid) = ObservationRange(query, cursor, limit);
        if (invalid != null) return invalid;

        // The next cursor is a response header, so it must be known before the first row is written: the end of the
        // page is looked up first (null when the limit does not end the page). The rows are then read up to that row
        // inclusive and without a row limit: should a row still turn up inside the range after the lookup, the page
        // gets one row longer, and the row the cursor names is always part of it.
        var pageEnd = await results.ObservationPageEndAsync(range.Rows(), limit, cancellationToken);
        var rows = pageEnd is { } last
            ? await results.OpenObservationsAsync(range.Rows(upTo: last), null, cancellationToken)
            : await results.OpenObservationsAsync(range.Rows(), limit, cancellationToken);
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

    /// <summary>What one page reads: the shared filters, after the cursor's row, and only settled rows.</summary>
    private sealed record PageRange(FilterDefinition<NetworkDiagnosticObservation> Filter, ObjectId? After, ObjectId Settled)
    {
        public FilterDefinition<NetworkDiagnosticObservation> Rows(ObjectId? upTo = null) => NetDiagFilters.IdRange(Filter, After, upTo, Settled);
    }

    /// <summary>The range of the page asked for, or the 400 for a bad filter, limit or cursor.</summary>
    private (PageRange Range, NetDiagResult Invalid) ObservationRange(NetDiagQuery query, string cursor, int limit)
    {
        query ??= new NetDiagQuery();
        if (limit is < 1 or > MaxObservationLimit) return (null, NetDiagResult.Error(400, "limit must be in 1-5000"));
        if (query.InvalidParameter() is { } key) return (null, AdminRequests.InvalidParameter(key));
        ObjectId? after = null;
        if (!string.IsNullOrEmpty(cursor))
        {
            // The cursor is the _id of the last row of the previous page. Anything that is not an ObjectId is refused;
            // a well-formed one only ever narrows the filter.
            if (!ObjectId.TryParse(cursor, out var parsed)) return (null, AdminRequests.InvalidParameter("cursor"));
            after = parsed;
        }
        return (new PageRange(NetDiagFilters.Observations(query), after, SettledBoundary(time.GetUtcNow())), null);
    }
}
