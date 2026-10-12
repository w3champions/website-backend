using System;
using System.Collections.Generic;
using System.Linq;
using MongoDB.Bson;

namespace W3ChampionsStatisticService.NetworkDiagnostics.Results;

public record AggregateRowDto(
    Dictionary<string, object> Key, int Count, double? P50MsMedian, double? P50MsP90, double? LossPctMedian,
    double ShareLossOver1Pct, double ShareUnreachable, string AsnOrg, double? Lat, double? Lon);

public record AggregateResponseDto(List<string> GroupBy, List<AggregateRowDto> Rows);

/// <summary>
/// C6 aggregate: Mongo groups and counts; medians and p90 are finalized here from the grouped values with the
/// StatsDto nearest-rank function (plan deviation 10).
/// </summary>
public static class AggregateBuilder
{
    public const int MaxRows = 1000;
    private const int MaxKeys = 3;
    private const double LossShareThresholdPct = 1;
    private const int ShareDecimals = 4;
    private const int PositionDecimals = 4;
    private const int ValueDecimals = 3;

    /// <summary>C6 groupBy key to observation field.</summary>
    public static readonly IReadOnlyDictionary<string, string> GroupKeys = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["country"] = nameof(NetworkDiagnosticObservation.Country),
        ["city"] = nameof(NetworkDiagnosticObservation.City),
        ["asn"] = nameof(NetworkDiagnosticObservation.Asn),
        ["os"] = nameof(NetworkDiagnosticObservation.Os),
        ["adapterType"] = nameof(NetworkDiagnosticObservation.PrimaryAdapterType),
        ["vpn"] = nameof(NetworkDiagnosticObservation.VpnSuspected),
        ["targetKey"] = nameof(NetworkDiagnosticObservation.TargetKey),
        ["path"] = nameof(NetworkDiagnosticObservation.Path),
        ["method"] = nameof(NetworkDiagnosticObservation.Method),
        ["runId"] = nameof(NetworkDiagnosticObservation.RunId),
    };

    /// <summary>The 1-3 distinct known keys of a comma-separated <c>groupBy</c>, or null when it holds anything else.</summary>
    public static List<string> ParseGroupBy(string groupBy)
    {
        var keys = (groupBy ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).ToList();
        return keys.Count is 0 or > MaxKeys || keys.Exists(key => !GroupKeys.ContainsKey(key)) ? null : keys;
    }

    /// <summary>
    /// The two <c>$group</c> specifications, in order. Rows are first folded per (key, measurement), then per key:
    /// the final group averages one position per measurement (the C6 centroid) and holds one value list per
    /// measurement, so it never lists measurement ids. A group over all <see cref="NetDiagConstants.MaxScannedObservations"/>
    /// rows stays far below the 16 MB document limit (pinned by AggregateBuilderTests).
    /// </summary>
    public static IReadOnlyList<BsonDocument> GroupStages(IReadOnlyList<string> keys)
    {
        var key = new BsonDocument();
        foreach (var name in keys) key.Add(name, "$" + GroupKeys[name]);
        var perMeasurement = new BsonDocument
        {
            { "_id", new BsonDocument { { "k", key }, { "m", "$" + nameof(NetworkDiagnosticObservation.MeasurementId) } } },
            { "count", new BsonDocument("$sum", 1) },
            { "p50s", new BsonDocument("$push", "$" + nameof(NetworkDiagnosticObservation.P50Ms)) },
            { "losses", new BsonDocument("$push", "$" + nameof(NetworkDiagnosticObservation.LossPct)) },
            { "lossRows", CountWhere(new BsonDocument("$isNumber", "$" + nameof(NetworkDiagnosticObservation.LossPct))) },
            { "lossOver1", CountWhere(new BsonDocument("$gt", new BsonArray { "$" + nameof(NetworkDiagnosticObservation.LossPct), LossShareThresholdPct })) },
            { "unreachable", CountWhere(new BsonDocument("$eq", new BsonArray { "$" + nameof(NetworkDiagnosticObservation.Outcome), NetDiagConstants.OutcomeUnreachable })) },
            // $max, not $first: it skips null, so rows without an ASN name do not blank the name of the group.
            { "asnOrg", new BsonDocument("$max", "$" + nameof(NetworkDiagnosticObservation.AsnOrg)) },
            { "lat", new BsonDocument("$first", "$" + nameof(NetworkDiagnosticObservation.Lat)) },
            { "lon", new BsonDocument("$first", "$" + nameof(NetworkDiagnosticObservation.Lon)) },
        };
        var perKey = new BsonDocument
        {
            { "_id", "$_id.k" },
            { "count", new BsonDocument("$sum", "$count") },
            { "p50s", new BsonDocument("$push", "$p50s") },
            { "losses", new BsonDocument("$push", "$losses") },
            { "lossRows", new BsonDocument("$sum", "$lossRows") },
            { "lossOver1", new BsonDocument("$sum", "$lossOver1") },
            { "unreachable", new BsonDocument("$sum", "$unreachable") },
            { "asnOrg", new BsonDocument("$max", "$asnOrg") },
            { "lat", new BsonDocument("$avg", "$lat") },
            { "lon", new BsonDocument("$avg", "$lon") },
        };
        return [perMeasurement, perKey];
    }

    /// <summary>One document of the final group (see <see cref="GroupStages"/>) as the C6 row.</summary>
    public static AggregateRowDto Finalize(BsonDocument row, IReadOnlyList<string> keys)
    {
        var id = row["_id"].AsBsonDocument;
        var key = keys.ToDictionary(k => k, k => id.TryGetValue(k, out var v) ? BsonTypeMapper.MapToDotNetValue(v) : null);
        var count = row["count"].ToInt32();
        var lossRows = row["lossRows"].ToInt32();
        var p50s = Sorted(row["p50s"].AsBsonArray);
        var losses = Sorted(row["losses"].AsBsonArray);

        return new AggregateRowDto(
            key,
            count,
            Round(SeriesStats.NearestRank(p50s, 50), ValueDecimals),
            Round(SeriesStats.NearestRank(p50s, 90), ValueDecimals),
            Round(SeriesStats.NearestRank(losses, 50), ValueDecimals),
            // Rows that sent nothing have no loss figure: they stay out of the denominator (but count as rows).
            lossRows == 0 ? 0 : Math.Round(row["lossOver1"].ToInt32() / (double)lossRows, ShareDecimals),
            Math.Round(row["unreachable"].ToInt32() / (double)count, ShareDecimals),
            keys.Contains("asn") && row.TryGetValue("asnOrg", out var org) && org.IsString ? org.AsString : null,
            Round(Number(row, "lat"), PositionDecimals),
            Round(Number(row, "lon"), PositionDecimals));
    }

    private static BsonDocument CountWhere(BsonDocument condition) =>
        new("$sum", new BsonDocument("$cond", new BsonArray { condition, 1, 0 }));

    /// <summary>The numeric values of the per-measurement lists, ascending.</summary>
    private static List<double> Sorted(BsonArray lists) =>
        lists.SelectMany(list => list.AsBsonArray).Where(v => v.IsNumeric).Select(v => v.ToDouble()).OrderBy(x => x).ToList();

    private static double? Number(BsonDocument row, string name) => row.TryGetValue(name, out var v) && v.IsNumeric ? v.ToDouble() : null;

    private static double? Round(double? value, int decimals) => value is { } x ? Math.Round(x, decimals) : null;
}
