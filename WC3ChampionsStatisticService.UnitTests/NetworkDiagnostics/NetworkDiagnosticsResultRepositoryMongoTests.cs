using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mongo2Go;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using W3ChampionsStatisticService.NetworkDiagnostics;
using W3ChampionsStatisticService.NetworkDiagnostics.Persistence;
using W3ChampionsStatisticService.NetworkDiagnostics.Results;
using W3ChampionsStatisticService.NetworkDiagnostics.Runs;
using W3ChampionsStatisticService.NetworkDiagnostics.Submission;

namespace WC3ChampionsStatisticService.Tests.NetworkDiagnostics;

[TestFixture]
public class NetworkDiagnosticsResultRepositoryMongoTests
{
    private static readonly DateTime Now = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);
    private MongoDbRunner _runner;
    private MongoClient _client;
    private NetworkDiagnosticsResultRepository _repo;

    [SetUp]
    public async Task SetUp()
    {
        _runner = MongoDbRunner.Start();
        _client = new MongoClient(_runner.ConnectionString);
        _repo = new NetworkDiagnosticsResultRepository(_client);
        await _repo.EnsureIndexesAsync();
    }

    [TearDown]
    public void TearDown() => _runner.Dispose();

    private static BuiltResult Built() => ResultBuilder.Build(
        SubmissionFixtures.Parse(SubmissionFixtures.FullJson),
        new NetworkDiagnosticAssignment { Id = SubmissionFixtures.MeasurementId, RunId = "run-1", BattleTag = "Name#1234" },
        Now, "81.2.69.160",
        new Dictionary<string, ClientGeo> { ["81.2.69.160"] = new() { Country = "GB", Asn = 20712 } });

    [TestCase(NetDiagConstants.MeasurementsCollection)]
    [TestCase(NetDiagConstants.RawSeriesCollection)]
    [TestCase(NetDiagConstants.IpInfoCollection)]
    [TestCase(NetDiagConstants.ObservationsCollection)]
    public async Task EnsureIndexes_EveryCollectionHasAZeroSecondTtl(string collection)
    {
        var indexes = await (await _client.GetDatabase("W3Champions-Statistic-Service")
            .GetCollection<BsonDocument>(collection).Indexes.ListAsync()).ToListAsync();

        Assert.IsTrue(indexes.Any(i => i["key"].AsBsonDocument.Contains("ExpiresAt") && i.GetValue("expireAfterSeconds", -1).ToInt64() == 0));
    }

    // Spec section 4.6 (P14): the observation lookup by target, path and method over time.
    [Test]
    public async Task EnsureIndexes_ObservationsHaveTheTargetPathMethodTimeIndex()
    {
        var indexes = await (await _client.GetDatabase("W3Champions-Statistic-Service")
            .GetCollection<BsonDocument>(NetDiagConstants.ObservationsCollection).Indexes.ListAsync()).ToListAsync();

        Assert.IsTrue(indexes.Any(i => i["key"].AsBsonDocument.Names.SequenceEqual(["TargetKey", "Path", "Method", "SubmittedAt"])));
    }

    [Test]
    public async Task InsertResult_RoundTripsMeasurementRawAndObservations()
    {
        var built = Built();
        await _repo.InsertResultAsync(built);

        var m = await _repo.GetMeasurementAsync(SubmissionFixtures.MeasurementId);
        Assert.AreEqual(15.0, m.Targets[0].Paths[0].Probes[0].Stats.P50Ms);
        Assert.AreEqual("Kaspersky", m.System.Antivirus.ThirdParty.Single());
        Assert.AreEqual(4, (await _repo.GetRawSeriesAsync(SubmissionFixtures.MeasurementId)).Series.Count);
        var observations = await _client.GetDatabase("W3Champions-Statistic-Service")
            .GetCollection<NetworkDiagnosticObservation>(NetDiagConstants.ObservationsCollection)
            .CountDocumentsAsync(o => o.MeasurementId == SubmissionFixtures.MeasurementId);
        Assert.AreEqual(built.Observations.Count, observations);
        Assert.AreEqual(4, observations);
        var geo = await _repo.ClientGeoByMeasurementAsync([SubmissionFixtures.MeasurementId, "missing"]);
        Assert.AreEqual("GB", geo[SubmissionFixtures.MeasurementId].Country);
        Assert.IsFalse(geo.ContainsKey("missing"));
    }

    [Test]
    public async Task UpsertIpInfo_KeepsTheFirstRowPerRunAndIp()
    {
        var first = ResultBuilder.IpInfoRows("run-1", new Dictionary<string, ClientGeo> { ["1.2.3.4"] = new() { Asn = 1, GeoDbBuild = "old" } }, Now);
        var second = ResultBuilder.IpInfoRows("run-1", new Dictionary<string, ClientGeo> { ["1.2.3.4"] = new() { Asn = 2, GeoDbBuild = "new" }, ["5.6.7.8"] = new() { Asn = 3 } }, Now.AddHours(1));

        await _repo.UpsertIpInfoAsync(first);
        await _repo.UpsertIpInfoAsync(second);

        var rows = await _repo.IpInfoAsync("run-1", ["1.2.3.4", "5.6.7.8"]);
        Assert.AreEqual(2, rows.Count);
        var kept = rows.Single(r => r.Ip == "1.2.3.4");
        Assert.AreEqual(1, kept.Asn);
        Assert.AreEqual(Now, kept.FirstSeenAt);
        Assert.IsEmpty(await _repo.IpInfoAsync("run-2", ["1.2.3.4"]));
    }

    private IMongoCollection<T> Collection<T>(string name) => _client.GetDatabase("W3Champions-Statistic-Service").GetCollection<T>(name);

    private Task InsertMeasurements(params NetworkDiagnosticMeasurement[] measurements) =>
        Collection<NetworkDiagnosticMeasurement>(NetDiagConstants.MeasurementsCollection).InsertManyAsync(measurements);

    private Task InsertObservations(params NetworkDiagnosticObservation[] observations) =>
        Collection<NetworkDiagnosticObservation>(NetDiagConstants.ObservationsCollection).InsertManyAsync(observations);

    private static NetworkDiagnosticMeasurement Measurement(string id, int hoursAfterNow, string country = "DE") => new()
    {
        Id = id,
        RunId = "run-1",
        BattleTag = "Name#1234",
        SubmittedAt = Now.AddHours(hoursAfterNow),
        ExpiresAt = Now.AddDays(365),
        ClientGeo = new ClientGeo { Country = country },
    };

    private static NetworkDiagnosticObservation Observation(string measurementId, int hoursAfterNow, string method = "TcpPing") => new()
    {
        Id = ObjectId.GenerateNewId(),
        MeasurementId = measurementId,
        RunId = "run-1",
        SubmittedAt = Now.AddHours(hoursAfterNow),
        ExpiresAt = Now.AddDays(365),
        TargetKey = "flo:12",
        Method = method,
    };

    // A5: zero-based page (skip page * pageSize), newest first, total over every match.
    [Test]
    public async Task ListMeasurements_NewestFirst_ZeroBasedPages_AndTheTotalOfAllMatches()
    {
        await InsertMeasurements(Measurement("m0", 0), Measurement("m1", 1), Measurement("m2", 2), Measurement("m3", 3), Measurement("m4", 4));
        var all = NetDiagFilters.Measurements(new NetDiagQuery(), null);

        var (first, total) = await _repo.ListMeasurementsAsync(all, 0, 2);
        var (second, _) = await _repo.ListMeasurementsAsync(all, 1, 2);
        var (third, _) = await _repo.ListMeasurementsAsync(all, 2, 2);
        var (beyond, totalBeyond) = await _repo.ListMeasurementsAsync(all, 3, 2);

        Assert.AreEqual(5, total);
        Assert.AreEqual(new[] { "m4", "m3" }, first.Select(m => m.Id).ToArray());
        Assert.AreEqual(new[] { "m2", "m1" }, second.Select(m => m.Id).ToArray());
        Assert.AreEqual(new[] { "m0" }, third.Select(m => m.Id).ToArray());
        Assert.IsEmpty(beyond);
        Assert.AreEqual(5, totalBeyond);
    }

    // A5 against real documents: from is inclusive, to is exclusive.
    [Test]
    public async Task ListMeasurements_TimeWindow_IncludesFrom_ExcludesTo()
    {
        await InsertMeasurements(Measurement("m0", 0), Measurement("m1", 1), Measurement("m2", 2), Measurement("m3", 3));
        var window = NetDiagFilters.Measurements(new NetDiagQuery { From = Now.AddHours(1), To = Now.AddHours(3) }, null);

        var (items, total) = await _repo.ListMeasurementsAsync(window, 0, 25);

        Assert.AreEqual(2, total);
        Assert.AreEqual(new[] { "m2", "m1" }, items.Select(m => m.Id).ToArray());
    }

    // The shared filter set against the documents a real submit writes: every field path exists as rendered.
    [Test]
    public async Task SharedFilters_MatchTheDocumentsOfARealResult()
    {
        await _repo.InsertResultAsync(Built());
        await InsertMeasurements(Measurement("other", 1, country: "GB"));
        await InsertObservations(Observation("other", 1, method: "IcmpMtr"));
        var query = new NetDiagQuery
        {
            BattleTag = "Name#1234",
            RunId = ["run-1", "run-9"],
            TargetKey = "flo:12",
            NodeId = 12,
            TargetIp = "1.2.3.4",
            Path = "direct",
            Method = "TcpPing",
            Country = "GB",
            Asn = 20712,
            Os = "windows",
            AdapterType = "ethernet",
            Vpn = true,
            From = Now,
            To = Now.AddSeconds(1),
        };

        var (ids, truncated) = await _repo.MeasurementIdsAsync(NetDiagFilters.Observations(query), 100);
        var (items, total) = await _repo.ListMeasurementsAsync(NetDiagFilters.Measurements(query, ids), 0, 25);

        Assert.AreEqual(new[] { SubmissionFixtures.MeasurementId }, ids.ToArray());
        Assert.IsFalse(truncated);
        Assert.AreEqual(1, total);
        Assert.AreEqual(SubmissionFixtures.MeasurementId, items.Single().Id);
        Assert.AreEqual(20712, items.Single().ClientGeo.Asn);

        // Each filter alone can also exclude the real result.
        foreach (var miss in new[]
                 {
                     query with { NodeId = 13 }, query with { Path = "proxied" }, query with { Method = "TcpTraceroute" },
                     query with { TargetIp = "9.9.9.9" }, query with { TargetKey = "flo:13" },
                 })
        {
            Assert.IsEmpty((await _repo.MeasurementIdsAsync(NetDiagFilters.Observations(miss), 100)).Ids, miss.ToString());
        }
        foreach (var miss in new[]
                 {
                     query with { Country = "DE" }, query with { Asn = 1 }, query with { Os = "macos" }, query with { AdapterType = "wifi" },
                     query with { Vpn = false }, query with { BattleTag = "Other#1" }, query with { RunId = ["run-9"] }, query with { To = Now },
                 })
        {
            Assert.AreEqual(0, (await _repo.ListMeasurementsAsync(NetDiagFilters.Measurements(miss, ids), 0, 25)).Total, miss.ToString());
        }
    }

    // P6: the lookup reads at most `limit` matching rows, newest first, and says when it read that many.
    [Test]
    public async Task MeasurementIds_ReadsOnlyTheNewestRowsUpToTheLimit_AndReportsReachingIt()
    {
        await InsertObservations(
            Observation("newest", 3), Observation("newest", 3),
            Observation("middle", 2),
            Observation("oldest", 1),
            Observation("other-method", 4, method: "IcmpMtr"));
        var filter = NetDiagFilters.Observations(new NetDiagQuery { Method = "TcpPing" });

        var all = await _repo.MeasurementIdsAsync(filter, 10);
        var three = await _repo.MeasurementIdsAsync(filter, 3);
        var two = await _repo.MeasurementIdsAsync(filter, 2);
        var exactly = await _repo.MeasurementIdsAsync(filter, 4);

        CollectionAssert.AreEquivalent(new[] { "newest", "middle", "oldest" }, all.Ids);
        Assert.IsFalse(all.Truncated);
        CollectionAssert.AreEquivalent(new[] { "newest", "middle" }, three.Ids);
        Assert.IsTrue(three.Truncated);
        CollectionAssert.AreEquivalent(new[] { "newest" }, two.Ids);
        Assert.IsTrue(two.Truncated);
        // Reading exactly `limit` rows cannot tell "all of them" from "more exist": it is reported as reached.
        CollectionAssert.AreEquivalent(new[] { "newest", "middle", "oldest" }, exactly.Ids);
        Assert.IsTrue(exactly.Truncated);
    }

    [Test]
    public async Task MeasurementIds_NoMatch_IsEmptyAndNotTruncated()
    {
        var (ids, truncated) = await _repo.MeasurementIdsAsync(NetDiagFilters.Observations(new NetDiagQuery { Method = "TcpPing" }), 10);

        Assert.IsEmpty(ids);
        Assert.IsFalse(truncated);
    }
}
