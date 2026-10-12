using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.NetworkDiagnostics.Api;
using W3ChampionsStatisticService.NetworkDiagnostics.Persistence;

namespace W3ChampionsStatisticService.NetworkDiagnostics.Results;

/// <summary>C6 read side. Nothing here sees an assignment or its token, so it may run behind the tracing proxy (A10).</summary>
public interface INetworkDiagnosticsQueryService
{
    /// <summary>
    /// Page is zero-based (A5). The filter set names a player and IPs, so it stays out of trace tags; the attribute
    /// sits on the interface because the tracing proxy reads it there. <c>total</c> of a filtered list stops at
    /// <see cref="NetDiagConstants.MaxCountedMeasurements"/>; an unfiltered list reports the estimated collection size.
    /// </summary>
    Task<NetDiagResult> ListMeasurementsAsync([NoTrace] NetDiagQuery query, int page, int pageSize);
    Task<NetDiagResult> GetMeasurementAsync(string id, bool raw);
}

[Trace]
public sealed partial class NetworkDiagnosticsQueryService(INetworkDiagnosticsResultRepository results, TimeProvider time) : INetworkDiagnosticsQueryService
{
    public async Task<NetDiagResult> ListMeasurementsAsync([NoTrace] NetDiagQuery query, int page, int pageSize)
    {
        query ??= new NetDiagQuery();
        var invalid = NetDiagPaging.Validate(page, pageSize);
        if (invalid != null) return invalid;
        if (query.InvalidParameter() is { } key) return AdminRequests.InvalidParameter(key);

        // A measurement has many targets, paths and methods: such filters are answered by its observation rows.
        var matching = query.HasTargetFilters
            ? await results.MeasurementIdsAsync(NetDiagFilters.Observations(query), NetDiagConstants.MaxScannedObservations)
            : default;
        var (items, total) = await results.ListMeasurementsAsync(NetDiagFilters.Measurements(query, matching.Ids), page, pageSize);
        var body = new PageDto<MeasurementSummaryDto>(items.Select(MeasurementDetailMapper.Summary).ToList(), total);
        return NetDiagResult.Ok(body) with { Truncated = matching.Truncated };
    }

    public async Task<NetDiagResult> GetMeasurementAsync(string id, bool raw)
    {
        var measurement = await results.GetMeasurementAsync(id);
        if (measurement == null) return NetDiagResult.Error(404, "not_found");
        var rawSeries = raw ? await results.GetRawSeriesAsync(id) : null;
        if (raw && rawSeries == null && time.GetUtcNow().UtcDateTime < RawExpiryOf(measurement))
        {
            // Inside the retention the series cannot have expired: they were never stored (the result is written in
            // three inserts without a transaction). That is "no samples", not "expired".
            rawSeries = new NetworkDiagnosticRawSeries { Id = id };
        }
        var ips = MeasurementDetailMapper.IpsOf(measurement);
        List<NetworkDiagnosticIpInfo> ipInfo = ips.Count == 0 ? [] : await results.IpInfoAsync(measurement.RunId, ips);
        return NetDiagResult.Ok(MeasurementDetailMapper.Detail(measurement, rawSeries, ipInfo, raw));
    }

    private static DateTime RawExpiryOf(NetworkDiagnosticMeasurement measurement) =>
        NetDiagTime.AsUtc(measurement.SubmittedAt) + NetDiagConstants.RawRetention;
}
