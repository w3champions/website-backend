using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace W3ChampionsStatisticService.NetworkDiagnostics.Results;

/// <summary>
/// C6 shared filter set. Bound with [FromQuery] as one complex model, so the permission filter's "battleTag" action
/// argument (the acting admin) cannot overwrite <see cref="BattleTag"/> (the player filtered for).
/// </summary>
public record NetDiagQuery
{
    public string BattleTag { get; init; }
    public List<string> RunId { get; init; }
    public List<string> MeasurementId { get; init; }
    public string TargetKey { get; init; }
    public int? NodeId { get; init; }
    public string TargetIp { get; init; }
    public string Path { get; init; }
    public string Method { get; init; }
    public string Country { get; init; }
    public string City { get; init; }
    public long? Asn { get; init; }
    public string Os { get; init; }
    public string AdapterType { get; init; }
    public bool? Vpn { get; init; }
    /// <summary>Inclusive (A5).</summary>
    public DateTime? From { get; init; }
    /// <summary>Exclusive (A5).</summary>
    public DateTime? To { get; init; }

    /// <summary>True when a filter names something only observations know: a measurement has many targets, paths and methods.</summary>
    public bool HasTargetFilters =>
        !string.IsNullOrEmpty(TargetKey) || NodeId != null || !string.IsNullOrEmpty(TargetIp)
        || !string.IsNullOrEmpty(Path) || !string.IsNullOrEmpty(Method);
}

public static class NetDiagFilters
{
    /// <summary>Where the client-level filters live in one document type; everything else about them is the same.</summary>
    private sealed record ClientFields<T>(
        Expression<Func<T, string>> BattleTag,
        Expression<Func<T, string>> RunId,
        Expression<Func<T, string>> MeasurementId,
        Expression<Func<T, string>> Country,
        Expression<Func<T, string>> City,
        Expression<Func<T, long?>> Asn,
        Expression<Func<T, string>> Os,
        Expression<Func<T, string>> AdapterType,
        Expression<Func<T, bool>> Vpn,
        Expression<Func<T, DateTime>> SubmittedAt);

    private static readonly ClientFields<NetworkDiagnosticObservation> ObservationFields = new(
        x => x.BattleTag, x => x.RunId, x => x.MeasurementId, x => x.Country, x => x.City, x => x.Asn,
        x => x.Os, x => x.PrimaryAdapterType, x => x.VpnSuspected, x => x.SubmittedAt);

    private static readonly ClientFields<NetworkDiagnosticMeasurement> MeasurementFields = new(
        x => x.BattleTag, x => x.RunId, x => x.Id, x => x.ClientGeo.Country, x => x.ClientGeo.City, x => x.ClientGeo.Asn,
        x => x.Os, x => x.PrimaryAdapterType, x => x.VpnSuspected, x => x.SubmittedAt);

    /// <summary>Every filter is a flat field of the observation row.</summary>
    public static FilterDefinition<NetworkDiagnosticObservation> Observations(NetDiagQuery q)
    {
        var f = Builders<NetworkDiagnosticObservation>.Filter;
        var parts = ClientLevel(q, ObservationFields, NullIfEmpty(q.MeasurementId));
        if (!string.IsNullOrEmpty(q.TargetKey)) parts.Add(f.Eq(x => x.TargetKey, q.TargetKey));
        if (q.NodeId != null) parts.Add(f.Eq(x => x.NodeId, q.NodeId));
        if (!string.IsNullOrEmpty(q.TargetIp)) parts.Add(f.Eq(x => x.TargetIp, q.TargetIp));
        if (!string.IsNullOrEmpty(q.Path)) parts.Add(f.Eq(x => x.Path, q.Path));
        if (!string.IsNullOrEmpty(q.Method)) parts.Add(f.Eq(x => x.Method, q.Method));
        return All(parts);
    }

    /// <param name="idsMatchingTargetFilters">
    /// Measurement ids whose observations match the query (see <see cref="Observations"/>); consulted only when the
    /// query has target-level filters, and then null means "none matched".
    /// </param>
    public static FilterDefinition<NetworkDiagnosticMeasurement> Measurements(NetDiagQuery q, IReadOnlyCollection<string> idsMatchingTargetFilters)
    {
        // One _id clause: the explicit ids intersected with the ids whose observations match the target filters.
        var ids = NullIfEmpty(q.MeasurementId);
        if (q.HasTargetFilters)
        {
            var matching = idsMatchingTargetFilters ?? [];
            ids = ids == null ? matching : ids.Intersect(matching).ToList();
        }
        return All(ClientLevel(q, MeasurementFields, ids));
    }

    /// <summary>The filter as the server receives it (tests and logs).</summary>
    public static BsonDocument Render<T>(FilterDefinition<T> filter) =>
        filter.Render(new RenderArgs<T>(BsonSerializer.SerializerRegistry.GetSerializer<T>(), BsonSerializer.SerializerRegistry));

    /// <param name="measurementIds">Null for no id clause; an empty collection matches nothing.</param>
    private static List<FilterDefinition<T>> ClientLevel<T>(NetDiagQuery q, ClientFields<T> fields, IReadOnlyCollection<string> measurementIds)
    {
        var f = Builders<T>.Filter;
        var parts = new List<FilterDefinition<T>>();
        if (!string.IsNullOrEmpty(q.BattleTag)) parts.Add(f.Eq(fields.BattleTag, q.BattleTag));
        if (q.RunId is { Count: > 0 }) parts.Add(f.In(fields.RunId, q.RunId));
        if (measurementIds != null) parts.Add(f.In(fields.MeasurementId, measurementIds));
        if (!string.IsNullOrEmpty(q.Country)) parts.Add(f.Eq(fields.Country, q.Country));
        if (!string.IsNullOrEmpty(q.City)) parts.Add(f.Eq(fields.City, q.City));
        if (q.Asn != null) parts.Add(f.Eq(fields.Asn, q.Asn));
        if (!string.IsNullOrEmpty(q.Os)) parts.Add(f.Eq(fields.Os, q.Os));
        if (!string.IsNullOrEmpty(q.AdapterType)) parts.Add(f.Eq(fields.AdapterType, q.AdapterType));
        if (q.Vpn is { } vpn) parts.Add(f.Eq(fields.Vpn, vpn));
        // A5: from inclusive, to exclusive, both UTC (a value without an offset is UTC, not server-local time).
        if (q.From is { } from) parts.Add(f.Gte(fields.SubmittedAt, NetDiagTime.AsUtc(from)));
        if (q.To is { } to) parts.Add(f.Lt(fields.SubmittedAt, NetDiagTime.AsUtc(to)));
        return parts;
    }

    private static FilterDefinition<T> All<T>(List<FilterDefinition<T>> parts) =>
        parts.Count == 0 ? Builders<T>.Filter.Empty : Builders<T>.Filter.And(parts);

    private static IReadOnlyCollection<string> NullIfEmpty(List<string> values) => values is { Count: > 0 } ? values : null;
}
