using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using MongoDB.Driver;

namespace W3ChampionsStatisticService.NetworkDiagnostics.Results;

/// <summary>
/// C6 ObservationDto. The parameter order IS the C6 field order: the JSON forms and the CSV header and cells are all
/// read from this one list (see <see cref="ObservationFormats"/>), pinned against the contract by ObservationFormatsTests.
/// </summary>
public record ObservationDto(
    string MeasurementId, string RunId, string BattleTag, DateTime SubmittedAt, string ClientIp,
    string Country, string City, double? Lat, double? Lon, long? Asn, string AsnOrg, string Os,
    string PrimaryAdapterType, bool? VpnSuspected, string TargetKey, string TargetLabel, string TargetIp, string Path, string RouteLabel, string Method,
    string Outcome, string Reason, int Sent, int Received, double? LossPct, double? MinMs, double? AvgMs, double? MaxMs, double? StddevMs,
    double? P50Ms, double? P90Ms, double? P95Ms, double? P99Ms,
    double? JitterMs, int HopCount, string LastHopIp, long? LastHopAsn)
{
    public static ObservationDto From(NetworkDiagnosticObservation o) => new(
        o.MeasurementId, o.RunId, o.BattleTag, NetDiagTime.AsUtc(o.SubmittedAt), o.ClientIp,
        o.Country, o.City, o.Lat, o.Lon, o.Asn, o.AsnOrg, o.Os,
        o.PrimaryAdapterType, o.VpnSuspected, o.TargetKey, o.TargetLabel, o.TargetIp, o.Path, o.RouteLabel, o.Method,
        o.Outcome, o.Reason, o.Sent, o.Received, o.LossPct, o.MinMs, o.AvgMs, o.MaxMs, o.StddevMs,
        o.P50Ms, o.P90Ms, o.P95Ms, o.P99Ms,
        o.JitterMs, o.HopCount, o.LastHopIp, o.LastHopAsn);
}

/// <summary>The JSON form of <c>GET observations</c>: <c>{ items, nextCursor }</c>.</summary>
public record ObservationPage(List<ObservationDto> Items, string NextCursor);

/// <summary>
/// The ndjson and csv forms: an open server cursor whose rows are written out batch by batch, and the next cursor,
/// which is known up front because it travels in a response header. Whoever receives it disposes <see cref="Rows"/>.
/// </summary>
public sealed record ObservationStream(IAsyncCursor<NetworkDiagnosticObservation> Rows, string NextCursor);

/// <summary>One line-based form of the observation list: what precedes the rows, and one row as a complete line.</summary>
public sealed record ObservationFormat(string ContentType, string Preamble, Func<ObservationDto, string> Line)
{
    public string Render(IEnumerable<ObservationDto> rows)
    {
        var text = new StringBuilder();
        foreach (var row in rows) text.Append(Line(row));
        return text.ToString();
    }
}

public static class ObservationFormats
{
    public const string JsonName = "json";
    public const string NdjsonName = "ndjson";
    public const string CsvName = "csv";

    private const string CsvLineEnd = "\r\n";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

    /// <summary>
    /// The serializer's own property list of <see cref="ObservationDto"/>: names and order are exactly what the JSON
    /// forms emit, so a column cannot exist in one form only.
    /// </summary>
    private static readonly IReadOnlyList<JsonPropertyInfo> Columns = [.. Json.GetTypeInfo(typeof(ObservationDto)).Properties];

    public static readonly IReadOnlyList<string> CsvColumns = [.. Columns.Select(column => column.Name)];

    public static readonly ObservationFormat Csv = new(
        "text/csv; charset=utf-8",
        string.Join(",", CsvColumns) + CsvLineEnd,
        row => string.Join(",", Columns.Select(column => Cell(column.Get!(row)))) + CsvLineEnd);

    public static readonly ObservationFormat Ndjson = new(
        "application/x-ndjson; charset=utf-8",
        "",
        row => JsonSerializer.Serialize(row, Json) + "\n");

    /// <summary>The format streamed for a <c>format</c> value, or null when it names neither line-based form.</summary>
    public static ObservationFormat Streamed(string format) => format switch
    {
        CsvName => Csv,
        NdjsonName => Ndjson,
        _ => null,
    };

    private static string Cell(object value) => value switch
    {
        null => "",
        string s => Quote(s),
        bool b => b ? "true" : "false",
        DateTime d => NetDiagTime.AsUtc(d).ToString("O", CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => Quote(value.ToString()),
    };

    // Review Focus 6: a text cell that a spreadsheet would read as a formula gets a leading ' (OWASP CSV injection).
    // Only string cells are touched, so a negative longitude (a double) stays numeric.
    private static readonly char[] FormulaTriggers = ['=', '+', '-', '@', '\t', '\r'];
    private static readonly char[] QuotingTriggers = [',', '"', '\r', '\n'];

    private static string Quote(string s)
    {
        if (s.Length > 0 && Array.IndexOf(FormulaTriggers, s[0]) >= 0) s = "'" + s;
        return s.IndexOfAny(QuotingTriggers) < 0 ? s : "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
