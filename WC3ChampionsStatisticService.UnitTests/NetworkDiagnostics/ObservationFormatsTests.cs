using System;
using System.Linq;
using System.Text.Json;
using MongoDB.Bson;
using NUnit.Framework;
using W3ChampionsStatisticService.NetworkDiagnostics.Results;

namespace WC3ChampionsStatisticService.Tests.NetworkDiagnostics;

[TestFixture]
public class ObservationFormatsTests
{
    // Verbatim from contracts C6 "Observations".
    private const string C6Columns = "measurementId, runId, battleTag, submittedAt, clientIp, country, city, lat, lon, asn, asnOrg, os, " +
        "primaryAdapterType, vpnSuspected, targetKey, targetLabel, targetIp, path, routeLabel, method, " +
        "outcome, reason, sent, received, lossPct, minMs, avgMs, maxMs, stddevMs, p50Ms, p90Ms, p95Ms, p99Ms, " +
        "jitterMs, hopCount, lastHopIp, lastHopAsn";

    private const int C6ColumnCount = 37;

    private static NetworkDiagnosticObservation Row() => new()
    {
        Id = ObjectId.GenerateNewId(),
        MeasurementId = "m1",
        RunId = "r1",
        BattleTag = "Name#1234",
        SubmittedAt = new DateTime(2026, 10, 12, 10, 2, 0, DateTimeKind.Utc),
        ClientIp = "81.2.69.160",
        Country = "GB",
        City = "London, City of",
        Lat = 51.5142,
        Lon = -0.0931,
        Asn = 20712,
        AsnOrg = "Andrews \"A&A\" Arnold",
        Os = "windows",
        PrimaryAdapterType = "ethernet",
        VpnSuspected = true,
        TargetKey = "flo:12",
        NodeId = 12,
        TargetLabel = "US East 4",
        TargetIp = "1.2.3.4",
        Path = "direct",
        Method = "IcmpMtr",
        Outcome = "ok",
        Sent = 10,
        Received = 9,
        LossPct = 10,
        MinMs = 11.5,
        P50Ms = 12.25,
        HopCount = 3,
        LastHopIp = "1.2.3.4",
        LastHopAsn = 13335,
    };

    [Test]
    public void CsvColumns_AreExactlyTheC6Order()
    {
        Assert.AreEqual(C6Columns.Split(", "), ObservationFormats.CsvColumns);
        Assert.AreEqual(C6ColumnCount, ObservationFormats.CsvColumns.Count);
    }

    [Test]
    public void ObservationDto_JsonPropertyOrder_MatchesTheCsvColumns()
    {
        var json = JsonSerializer.Serialize(ObservationDto.From(Row()), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var names = JsonDocument.Parse(json).RootElement.EnumerateObject().Select(p => p.Name).ToArray();

        Assert.AreEqual(ObservationFormats.CsvColumns, names);
    }

    [Test]
    public void Csv_HeaderThenRows_InvariantCulture_Rfc4180Quoting_EmptyNulls()
    {
        var csv = ObservationText.ToCsv([ObservationDto.From(Row())]);
        var lines = csv.Split("\r\n");

        Assert.AreEqual(string.Join(",", ObservationFormats.CsvColumns), lines[0]);
        StringAssert.StartsWith("m1,r1,Name#1234,2026-10-12T10:02:00.0000000Z,81.2.69.160,GB,\"London, City of\",51.5142,-0.0931,20712,\"Andrews \"\"A&A\"\" Arnold\",windows,ethernet,true,flo:12,", lines[1]);
        StringAssert.Contains(",ok,,10,9,10,11.5,,,,12.25,,,,,3,1.2.3.4,13335", lines[1]);
        Assert.AreEqual("", lines[2], "the file ends with a line break");
    }

    [Test]
    public void UnknownVpn_IsNullInJson_NullInNdjson_AndAnEmptyCsvCell()
    {
        var dto = ObservationDto.From(Row() with { VpnSuspected = null, City = "London" });
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, options));
        using var ndjson = JsonDocument.Parse(ObservationFormats.Ndjson.Line(dto));
        var header = ObservationFormats.CsvColumns.ToList();
        var csvCells = ObservationFormats.Csv.Line(dto).TrimEnd('\r', '\n').Split(',');

        Assert.AreEqual(JsonValueKind.Null, json.RootElement.GetProperty("vpnSuspected").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, ndjson.RootElement.GetProperty("vpnSuspected").ValueKind);
        Assert.AreEqual("ethernet,,flo:12", string.Join(",", csvCells.Skip(header.IndexOf("primaryAdapterType")).Take(3)));
    }

    [Test]
    public void KnownVpn_RendersTrueAndFalse()
    {
        var idx = ObservationFormats.CsvColumns.ToList().IndexOf("vpnSuspected");

        Assert.AreEqual("true", ObservationFormats.Csv.Line(ObservationDto.From(Row() with { City = "London" })).Split(',')[idx]);
        Assert.AreEqual("false", ObservationFormats.Csv.Line(ObservationDto.From(Row() with { VpnSuspected = false, City = "London" })).Split(',')[idx]);
    }

    // One list drives the header and the cells: a row cannot have more or fewer cells than the header has columns.
    [Test]
    public void Csv_EveryRowHasOneCellPerColumn()
    {
        var plain = Row() with { City = "London", AsnOrg = "AAISP" };

        var lines = ObservationText.ToCsv([ObservationDto.From(plain), ObservationDto.From(new NetworkDiagnosticObservation())]).Split("\r\n");

        Assert.AreEqual(C6ColumnCount, lines[0].Split(',').Length);
        Assert.AreEqual(C6ColumnCount, lines[1].Split(',').Length);
        Assert.AreEqual(C6ColumnCount, lines[2].Split(',').Length, "a row of nulls keeps every cell");
    }

    // Review Focus 6.
    [TestCase("=HYPERLINK(\"http://x\")", "\"'=HYPERLINK(\"\"http://x\"\")\"")]
    [TestCase("+1", "'+1")]
    [TestCase("-cmd", "'-cmd")]
    [TestCase("@SUM(A1)", "'@SUM(A1)")]
    [TestCase("\tcmd", "'\tcmd")]
    [TestCase("\rcmd", "\"'\rcmd\"")]
    [TestCase("ok", "ok")]
    [TestCase("a=b", "a=b")]
    public void Csv_FormulaLikeCells_ArePrefixedWithAQuote(string value, string expectedCell)
    {
        var csv = ObservationText.ToCsv([ObservationDto.From(Row() with { Reason = value })]);

        StringAssert.Contains($",ok,{expectedCell},10,9,", csv[(csv.IndexOf("\r\n", StringComparison.Ordinal) + 2)..]);
        StringAssert.Contains(",51.5142,-0.0931,", csv, "numeric cells are never prefixed");
    }

    // Review Focus 6: the guard belongs to the cell type, not to one column. BattleTag, city, ASN org and labels
    // come from players or third-party data just like the reason.
    [Test]
    public void Csv_FormulaLikeCells_ArePrefixedInEveryStringColumn()
    {
        var hostile = Row() with
        {
            MeasurementId = "=m",
            RunId = "+r",
            BattleTag = "=cmd#1234",
            ClientIp = "-ip",
            Country = "@C",
            City = "=city",
            AsnOrg = "+org",
            Os = "-os",
            PrimaryAdapterType = "@adapter",
            TargetKey = "=key",
            TargetLabel = "-label",
            TargetIp = "+ip",
            Path = "@path",
            RouteLabel = "=route",
            Method = "-method",
            Outcome = "+outcome",
            Reason = "@reason",
            LastHopIp = "=hop",
            Lat = -1.5,
            Lon = -2.5,
        };

        var cells = ObservationText.ToCsv([ObservationDto.From(hostile)]).Split("\r\n")[1].Split(',');

        var stringColumns = typeof(ObservationDto).GetProperties().Where(p => p.PropertyType == typeof(string)).Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name)).ToList();
        Assert.AreEqual(18, stringColumns.Count);
        foreach (var column in stringColumns)
        {
            StringAssert.StartsWith("'", cells[ObservationFormats.CsvColumns.ToList().IndexOf(column)], column);
        }
        Assert.AreEqual("-1.5", cells[ObservationFormats.CsvColumns.ToList().IndexOf("lat")]);
        Assert.AreEqual("-2.5", cells[ObservationFormats.CsvColumns.ToList().IndexOf("lon")]);
    }

    [Test]
    public void Ndjson_OneCamelCaseObjectPerLine()
    {
        var lines = ObservationText.ToNdjson([ObservationDto.From(Row()), ObservationDto.From(Row())]).Split('\n');

        Assert.AreEqual(3, lines.Length);
        Assert.AreEqual("m1", JsonDocument.Parse(lines[1]).RootElement.GetProperty("measurementId").GetString());
        Assert.AreEqual("", lines[2], "every row ends with a line break");
    }

    [TestCase("csv", "text/csv; charset=utf-8")]
    [TestCase("ndjson", "application/x-ndjson; charset=utf-8")]
    public void Streamed_KnowsTheTwoLineFormats(string name, string contentType) =>
        Assert.AreEqual(contentType, ObservationFormats.Streamed(name).ContentType);

    [TestCase("json")]
    [TestCase("CSV")]
    [TestCase("xml")]
    [TestCase("")]
    [TestCase(null)]
    public void Streamed_AnythingElse_IsNull(string name) => Assert.IsNull(ObservationFormats.Streamed(name));
}
