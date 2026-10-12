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
}
