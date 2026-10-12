using System;
using System.Linq;
using MongoDB.Bson;
using NUnit.Framework;
using W3ChampionsStatisticService.NetworkDiagnostics.Results;

namespace WC3ChampionsStatisticService.Tests.NetworkDiagnostics;

[TestFixture]
public class NetDiagQueryTests
{
    private static readonly DateTime From = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private static NetDiagQuery Full() => new()
    {
        BattleTag = "Name#1234",
        RunId = ["r1", "r2"],
        MeasurementId = ["m1"],
        TargetKey = "flo:12",
        NodeId = 12,
        TargetIp = "1.2.3.4",
        Path = "direct",
        Method = "IcmpMtr",
        Country = "DE",
        City = "Berlin",
        Asn = 3320,
        Os = "windows",
        AdapterType = "wifi",
        Vpn = false,
        From = From,
        To = From.AddDays(1),
    };

    [Test]
    public void Observations_MapsEveryFilterToItsFlatField()
    {
        var rendered = NetDiagFilters.Render(NetDiagFilters.Observations(Full()));

        Assert.AreEqual("Name#1234", rendered["BattleTag"].AsString);
        Assert.AreEqual(new BsonArray { "r1", "r2" }, rendered["RunId"]["$in"]);
        Assert.AreEqual(new BsonArray { "m1" }, rendered["MeasurementId"]["$in"]);
        Assert.AreEqual("flo:12", rendered["TargetKey"].AsString);
        Assert.AreEqual(12, rendered["NodeId"].ToInt32());
        Assert.AreEqual("1.2.3.4", rendered["TargetIp"].AsString);
        Assert.AreEqual("direct", rendered["Path"].AsString);
        Assert.AreEqual("IcmpMtr", rendered["Method"].AsString);
        Assert.AreEqual("DE", rendered["Country"].AsString);
        Assert.AreEqual("Berlin", rendered["City"].AsString);
        Assert.AreEqual(3320L, rendered["Asn"].ToInt64());
        Assert.AreEqual("windows", rendered["Os"].AsString);
        Assert.AreEqual("wifi", rendered["PrimaryAdapterType"].AsString);
        Assert.AreEqual(false, rendered["VpnSuspected"].AsBoolean);
        Assert.AreEqual(From, rendered["SubmittedAt"]["$gte"].ToUniversalTime());
        Assert.AreEqual(From.AddDays(1), rendered["SubmittedAt"]["$lt"].ToUniversalTime());
        Assert.AreEqual(15, rendered.ElementCount, "one clause per filter, the two time bounds on one field");
    }

    // The measurement document keeps the client's geo nested and has no per-target fields.
    [Test]
    public void Measurements_MapsEveryClientLevelFilter_AndNoTargetLevelField()
    {
        var rendered = NetDiagFilters.Render(NetDiagFilters.Measurements(Full(), ["m1"]));

        Assert.AreEqual("Name#1234", rendered["BattleTag"].AsString);
        Assert.AreEqual(new BsonArray { "r1", "r2" }, rendered["RunId"]["$in"]);
        Assert.AreEqual(new BsonArray { "m1" }, rendered["_id"]["$in"]);
        Assert.AreEqual("DE", rendered["ClientGeo.Country"].AsString);
        Assert.AreEqual("Berlin", rendered["ClientGeo.City"].AsString);
        Assert.AreEqual(3320L, rendered["ClientGeo.Asn"].ToInt64());
        Assert.AreEqual("windows", rendered["Os"].AsString);
        Assert.AreEqual("wifi", rendered["PrimaryAdapterType"].AsString);
        Assert.AreEqual(false, rendered["VpnSuspected"].AsBoolean);
        Assert.AreEqual(From, rendered["SubmittedAt"]["$gte"].ToUniversalTime());
        Assert.AreEqual(From.AddDays(1), rendered["SubmittedAt"]["$lt"].ToUniversalTime());
        Assert.AreEqual(10, rendered.ElementCount, "target-level filters reach the measurement only through the _id clause");
    }

    // A5: from is inclusive ($gte), to is exclusive ($lt), both compared in UTC. No $gt/$lte anywhere.
    [Test]
    public void TimeWindow_FromInclusive_ToExclusive_InBothFilterBuilders()
    {
        var q = new NetDiagQuery { From = From, To = From.AddHours(1) };

        foreach (var rendered in new[]
                 {
                     NetDiagFilters.Render(NetDiagFilters.Observations(q)),
                     NetDiagFilters.Render(NetDiagFilters.Measurements(q, null)),
                 })
        {
            var window = rendered["SubmittedAt"].AsBsonDocument;
            Assert.AreEqual(new[] { "$gte", "$lt" }, window.Names.ToArray());
            Assert.AreEqual(From, window["$gte"].ToUniversalTime());
            Assert.AreEqual(From.AddHours(1), window["$lt"].ToUniversalTime());
        }
    }

    // A value without an offset ("2026-10-01T00:00:00") is UTC, whatever the server's time zone; a local one is converted.
    [Test]
    public void TimeWindow_LocalAndUnspecifiedTimes_BecomeTheSameUtcInstant()
    {
        var q = new NetDiagQuery { From = From.ToLocalTime(), To = DateTime.SpecifyKind(From, DateTimeKind.Unspecified) };

        foreach (var rendered in new[]
                 {
                     NetDiagFilters.Render(NetDiagFilters.Observations(q)),
                     NetDiagFilters.Render(NetDiagFilters.Measurements(q, null)),
                 })
        {
            Assert.AreEqual(From, rendered["SubmittedAt"]["$gte"].ToUniversalTime());
            Assert.AreEqual(From, rendered["SubmittedAt"]["$lt"].ToUniversalTime());
        }
    }

    [Test]
    public void EmptyQuery_MatchesEverything()
    {
        Assert.AreEqual(new BsonDocument(), NetDiagFilters.Render(NetDiagFilters.Observations(new NetDiagQuery())));
        Assert.AreEqual(new BsonDocument(), NetDiagFilters.Render(NetDiagFilters.Measurements(new NetDiagQuery(), null)));
    }

    // An empty value (?battleTag=&runId=) is no filter.
    [Test]
    public void EmptyStringsAndEmptyLists_AreNoFilter()
    {
        var q = new NetDiagQuery { BattleTag = "", RunId = [], MeasurementId = [], TargetKey = "", Country = "", Os = "" };

        Assert.AreEqual(new BsonDocument(), NetDiagFilters.Render(NetDiagFilters.Observations(q)));
        Assert.AreEqual(new BsonDocument(), NetDiagFilters.Render(NetDiagFilters.Measurements(q, null)));
        Assert.IsFalse(q.HasTargetFilters);
    }

    [Test]
    public void Measurements_UsesNestedGeoFields_AndIntersectsTargetMatches()
    {
        var q = new NetDiagQuery { Country = "DE", Asn = 3320, MeasurementId = ["m1", "m2"], TargetKey = "flo:12" };

        var rendered = NetDiagFilters.Render(NetDiagFilters.Measurements(q, ["m2", "m3"]));

        Assert.AreEqual("DE", rendered["ClientGeo.Country"].AsString);
        Assert.AreEqual(3320L, rendered["ClientGeo.Asn"].ToInt64());
        Assert.AreEqual(new BsonArray { "m2" }, rendered["_id"]["$in"], "explicit ids ∩ ids matching the target filters");
        Assert.IsFalse(rendered.Contains("TargetKey"));
    }

    [Test]
    public void Measurements_TargetFilterOnly_UsesTheMatchingIds() =>
        Assert.AreEqual(new BsonArray { "m2", "m3" },
            NetDiagFilters.Render(NetDiagFilters.Measurements(new NetDiagQuery { Method = "TcpPing" }, ["m2", "m3"]))["_id"]["$in"]);

    // Without a target-level filter the looked-up ids are not consulted at all.
    [Test]
    public void Measurements_NoTargetFilter_IgnoresTheMatchingIds() =>
        Assert.AreEqual(new BsonArray { "m1" },
            NetDiagFilters.Render(NetDiagFilters.Measurements(new NetDiagQuery { MeasurementId = ["m1"] }, ["m9"]))["_id"]["$in"]);

    [Test]
    public void Measurements_TargetFilterWithNoMatches_MatchesNothing()
    {
        Assert.AreEqual(new BsonArray(), NetDiagFilters.Render(NetDiagFilters.Measurements(new NetDiagQuery { Method = "TcpPing" }, []))["_id"]["$in"]);
        Assert.AreEqual(new BsonArray(), NetDiagFilters.Render(NetDiagFilters.Measurements(new NetDiagQuery { Method = "TcpPing" }, null))["_id"]["$in"]);
    }

    [Test]
    public void HasTargetFilters()
    {
        Assert.IsFalse(new NetDiagQuery { Country = "DE", RunId = ["r"] }.HasTargetFilters);
        Assert.IsTrue(new NetDiagQuery { TargetKey = "flo:12" }.HasTargetFilters);
        Assert.IsTrue(new NetDiagQuery { NodeId = 3 }.HasTargetFilters);
        Assert.IsTrue(new NetDiagQuery { TargetIp = "1.2.3.4" }.HasTargetFilters);
        Assert.IsTrue(new NetDiagQuery { Path = "direct" }.HasTargetFilters);
        Assert.IsTrue(new NetDiagQuery { Method = "TcpPing" }.HasTargetFilters);
    }
}
