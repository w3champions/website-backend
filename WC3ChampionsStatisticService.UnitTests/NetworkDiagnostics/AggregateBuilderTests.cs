using System.Linq;
using MongoDB.Bson;
using NUnit.Framework;
using W3ChampionsStatisticService.NetworkDiagnostics;
using W3ChampionsStatisticService.NetworkDiagnostics.Results;

namespace WC3ChampionsStatisticService.Tests.NetworkDiagnostics;

[TestFixture]
public class AggregateBuilderTests
{
    [TestCase("asn,path", new[] { "asn", "path" })]
    [TestCase(" country , method ", new[] { "country", "method" })]
    [TestCase("asn,asn", new[] { "asn" })]
    [TestCase("country,city,asn", new[] { "country", "city", "asn" })]
    public void ParseGroupBy_Valid(string input, string[] expected) =>
        Assert.AreEqual(expected, AggregateBuilder.ParseGroupBy(input));

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" , ")]
    [TestCase("asn,path,method,os")]
    [TestCase("asn,battleTag")]
    [TestCase("ASN")]
    [TestCase("$where")]
    public void ParseGroupBy_Invalid_IsNull(string input) => Assert.IsNull(AggregateBuilder.ParseGroupBy(input));

    [Test]
    public void GroupKeys_AreTheTenC6Keys() =>
        CollectionAssert.AreEquivalent(
            new[] { "country", "city", "asn", "os", "adapterType", "vpn", "targetKey", "path", "method", "runId" },
            AggregateBuilder.GroupKeys.Keys);

    // Stage one folds the rows of one measurement inside one group, so the centroid can be a mean over measurements
    // without the group document listing every measurement id.
    [Test]
    public void GroupStages_FirstGroupsPerKeyAndMeasurement_OnTheFlatFields()
    {
        var stage = AggregateBuilder.GroupStages(["asn", "adapterType", "vpn"])[0];

        Assert.AreEqual(
            new BsonDocument
            {
                { "k", new BsonDocument { { "asn", "$Asn" }, { "adapterType", "$PrimaryAdapterType" }, { "vpn", "$VpnSuspected" } } },
                { "m", "$MeasurementId" },
            },
            stage["_id"]);
        Assert.AreEqual(new BsonDocument("$sum", 1), stage["count"]);
        Assert.AreEqual(new BsonDocument("$push", "$P50Ms"), stage["p50s"]);
        Assert.AreEqual(new BsonDocument("$push", "$LossPct"), stage["losses"]);
        // $max skips null: one measurement without an ASN name must not blank the name of the group.
        Assert.AreEqual(new BsonDocument("$max", "$AsnOrg"), stage["asnOrg"]);
        Assert.AreEqual(new BsonDocument("$first", "$Lat"), stage["lat"]);
        Assert.AreEqual(new BsonDocument("$first", "$Lon"), stage["lon"]);
    }

    [Test]
    public void GroupStages_ThenGroupPerKey_SummingCountsAndAveragingThePositionsOfTheMeasurements()
    {
        var stages = AggregateBuilder.GroupStages(["asn"]);

        Assert.AreEqual(2, stages.Count);
        var stage = stages[1];
        Assert.AreEqual((BsonValue)"$_id.k", stage["_id"]);
        Assert.AreEqual(new BsonDocument("$sum", "$count"), stage["count"]);
        Assert.AreEqual(new BsonDocument("$push", "$p50s"), stage["p50s"]);
        Assert.AreEqual(new BsonDocument("$push", "$losses"), stage["losses"]);
        Assert.AreEqual(new BsonDocument("$max", "$asnOrg"), stage["asnOrg"]);
        Assert.AreEqual(new BsonDocument("$avg", "$lat"), stage["lat"]);
        Assert.AreEqual(new BsonDocument("$avg", "$lon"), stage["lon"]);
        // Nothing in the final group grows with the number of measurements except the two value lists.
        CollectionAssert.AreEquivalent(
            new[] { "_id", "count", "p50s", "losses", "lossOver1", "unreachable", "asnOrg", "lat", "lon" },
            stage.Names.ToArray());
    }

    [Test]
    public void Finalize_ComputesNearestRankMediansSharesAndCentroid()
    {
        var row = new BsonDocument
        {
            { "_id", new BsonDocument { { "asn", 3320L }, { "path", "direct" } } },
            { "count", 4 },
            // One inner list per measurement.
            { "p50s", new BsonArray { new BsonArray { 10.0, 30.0 }, new BsonArray { BsonNull.Value, 20.0 } } },
            { "losses", new BsonArray { new BsonArray { 0.0, 0.0 }, new BsonArray { 5.0, BsonNull.Value } } },
            { "lossOver1", 1 },
            { "unreachable", 1 },
            { "asnOrg", "Deutsche Telekom AG" },
            { "lat", 53.0 },
            { "lon", 12.0 },
        };

        var dto = AggregateBuilder.Finalize(row, ["asn", "path"]);

        Assert.AreEqual(3320L, dto.Key["asn"]);
        Assert.AreEqual("direct", dto.Key["path"]);
        Assert.AreEqual(4, dto.Count);
        Assert.AreEqual(20.0, dto.P50MsMedian);      // sorted 10,20,30: rank ceil(1.5)=2
        Assert.AreEqual(30.0, dto.P50MsP90);         // rank ceil(2.7)=3
        Assert.AreEqual(0.0, dto.LossPctMedian);     // sorted 0,0,5: rank 2
        Assert.AreEqual(0.25, dto.ShareLossOver1Pct);
        Assert.AreEqual(0.25, dto.ShareUnreachable);
        Assert.AreEqual("Deutsche Telekom AG", dto.AsnOrg);
        Assert.AreEqual(53.0, dto.Lat);
        Assert.AreEqual(12.0, dto.Lon);
    }

    [Test]
    public void Finalize_AsnOrgOnlyWhenGroupingByAsn_AndNullStatsWhenNoValues()
    {
        var row = new BsonDocument
        {
            { "_id", new BsonDocument("country", BsonNull.Value) },
            { "count", 2 },
            { "p50s", new BsonArray { new BsonArray { BsonNull.Value }, new BsonArray() } },
            { "losses", new BsonArray() },
            { "lossOver1", 0 },
            { "unreachable", 2 },
            { "asnOrg", "X" },
            { "lat", BsonNull.Value },
            { "lon", BsonNull.Value },
        };

        var dto = AggregateBuilder.Finalize(row, ["country"]);

        Assert.IsNull(dto.Key["country"]);
        Assert.IsNull(dto.AsnOrg);
        Assert.IsNull(dto.P50MsMedian);
        Assert.IsNull(dto.P50MsP90);
        Assert.IsNull(dto.LossPctMedian);
        Assert.IsNull(dto.Lat);
        Assert.IsNull(dto.Lon);
        Assert.AreEqual(1.0, dto.ShareUnreachable);
    }

    // A field no row of the group has is absent from the group id (not null): the key is still listed.
    [Test]
    public void Finalize_KeyFieldAbsentFromTheGroupId_IsNull()
    {
        var row = new BsonDocument
        {
            { "_id", new BsonDocument() },
            { "count", 1 },
            { "p50s", new BsonArray() },
            { "losses", new BsonArray() },
            { "lossOver1", 0 },
            { "unreachable", 0 },
        };

        var dto = AggregateBuilder.Finalize(row, ["city"]);

        Assert.IsTrue(dto.Key.ContainsKey("city"));
        Assert.IsNull(dto.Key["city"]);
        Assert.IsNull(dto.Lat);
    }

    // The same nearest-rank function as StatsDto (plan deviation 10): one implementation, two callers.
    [Test]
    public void Finalize_Percentiles_AreTheStatsDtoNearestRank()
    {
        var values = Enumerable.Range(1, 10).Select(i => (double)i).ToList();
        var row = new BsonDocument
        {
            { "_id", new BsonDocument("path", "direct") },
            { "count", 10 },
            { "p50s", new BsonArray { new BsonArray(values.AsEnumerable().Reverse()) } },
            { "losses", new BsonArray() },
            { "lossOver1", 0 },
            { "unreachable", 0 },
        };

        var dto = AggregateBuilder.Finalize(row, ["path"]);

        Assert.AreEqual(SeriesStats.NearestRank(values, 50), dto.P50MsMedian);
        Assert.AreEqual(SeriesStats.NearestRank(values, 90), dto.P50MsP90);
        Assert.AreEqual((5.0, 9.0), (dto.P50MsMedian, dto.P50MsP90));
    }

    // P6: the largest document the pipeline can build is a final group holding every scanned row. Its size is the
    // two value lists (8 bytes a value plus list overhead); nothing else in it grows with rows or measurements.
    [TestCase(200_000, 1, Description = "every row its own measurement: the most inner lists")]
    [TestCase(84, 2_400, Description = "the largest measurements (200 targets x 2 paths x 6 probes): the longest element names")]
    [TestCase(2_000, 100)]
    public void FinalGroupDocument_AtTheScanLimit_StaysUnderTheBsonDocumentLimit(int measurements, int rowsPerMeasurement)
    {
        const int BsonDocumentLimitBytes = 16 * 1024 * 1024;
        const int MaxStringLength = 500;
        Assert.GreaterOrEqual(measurements * rowsPerMeasurement, NetDiagConstants.MaxScannedObservations);
        BsonArray Lists() => new(Enumerable.Range(0, measurements).Select(_ => new BsonArray(Enumerable.Repeat((BsonValue)123.456, rowsPerMeasurement))));
        var longest = new string('x', MaxStringLength);
        var group = new BsonDocument
        {
            { "_id", new BsonDocument { { "city", longest }, { "targetKey", longest }, { "runId", longest } } },
            { "count", measurements * rowsPerMeasurement },
            { "p50s", Lists() },
            { "losses", Lists() },
            { "lossOver1", measurements * rowsPerMeasurement },
            { "unreachable", measurements * rowsPerMeasurement },
            { "asnOrg", longest },
            { "lat", 52.1 },
            { "lon", 13.1 },
        };
        CollectionAssert.AreEquivalent(AggregateBuilder.GroupStages(["city", "targetKey", "runId"])[1].Names.ToArray(), group.Names.ToArray());

        var bytes = group.ToBson().Length;

        Assert.Less(bytes, BsonDocumentLimitBytes * 3 / 4, $"{bytes} bytes");
    }
}
