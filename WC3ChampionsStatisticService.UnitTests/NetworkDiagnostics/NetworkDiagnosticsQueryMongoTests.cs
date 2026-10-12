using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mongo2Go;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Events;
using NUnit.Framework;
using W3ChampionsStatisticService.NetworkDiagnostics;
using W3ChampionsStatisticService.NetworkDiagnostics.Persistence;
using W3ChampionsStatisticService.NetworkDiagnostics.Results;

namespace WC3ChampionsStatisticService.Tests.NetworkDiagnostics;

/// <summary>The observation page, the observation stream and the aggregate pipeline against a real mongod (Mongo2Go).</summary>
[TestFixture]
public class NetworkDiagnosticsQueryMongoTests
{
    private static readonly DateTime Now = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string[] Seeded = ["m0", "m1", "m2", "m3", "m4", "m9"];
    private MongoDbRunner _runner;
    private IMongoCollection<NetworkDiagnosticObservation> _observations;
    private NetworkDiagnosticsResultRepository _repo;
    private NetworkDiagnosticsQueryService _service;
    private ConcurrentQueue<BsonDocument> _commands;
    private FixedTimeProvider _time;

    [SetUp]
    public async Task SetUp()
    {
        _runner = MongoDbRunner.Start();
        _commands = new ConcurrentQueue<BsonDocument>();
        var settings = MongoClientSettings.FromConnectionString(_runner.ConnectionString);
        // Every command as it goes to the server.
        settings.ClusterConfigurator = cluster => cluster.Subscribe<CommandStartedEvent>(e => _commands.Enqueue(e.Command.ToBsonDocument().DeepClone().AsBsonDocument));
        var client = new MongoClient(settings);
        _repo = new NetworkDiagnosticsResultRepository(client);
        await _repo.EnsureIndexesAsync();
        // The seeded rows get ids of the real clock; an hour later every one of them is settled.
        _time = new FixedTimeProvider(DateTimeOffset.UtcNow.AddHours(1));
        _service = new NetworkDiagnosticsQueryService(_repo, _time);
        _observations = client.GetDatabase("W3Champions-Statistic-Service").GetCollection<NetworkDiagnosticObservation>(NetDiagConstants.ObservationsCollection);

        var rows = new List<NetworkDiagnosticObservation>();
        for (var i = 0; i < 5; i++)
        {
            rows.Add(Obs($"m{i}", asn: 3320, path: "direct", p50: 10 + i, loss: i == 4 ? 5 : 0, lat: 52, lon: 13));
        }
        rows.Add(Obs("m9", asn: 1221, path: "relay", p50: 100, loss: 0, lat: -33, lon: 151, outcome: "unreachable"));
        await _observations.InsertManyAsync(rows);
    }

    [TearDown]
    public void TearDown() => _runner.Dispose();

    private static NetworkDiagnosticObservation Obs(
        string m, long asn, string path, double p50, double loss, double lat, double lon, string outcome = "ok", string run = "r1", int minutesAfterNow = 0) => new()
        {
            Id = ObjectId.GenerateNewId(),
            MeasurementId = m,
            RunId = run,
            BattleTag = "A#1",
            SubmittedAt = Now.AddMinutes(minutesAfterNow),
            Asn = asn,
            AsnOrg = asn == 3320 ? "Deutsche Telekom AG" : "Telstra",
            Path = path,
            Method = "IcmpMtr",
            Outcome = outcome,
            TargetKey = "flo:12",
            P50Ms = p50,
            LossPct = loss,
            Lat = lat,
            Lon = lon,
            Sent = 10,
            ExpiresAt = Now.AddDays(365),
        };

    /// <summary>The commands sent on the observation collection since the last call, oldest first.</summary>
    private List<BsonDocument> Commands()
    {
        var sent = new List<BsonDocument>();
        while (_commands.TryDequeue(out var command))
        {
            var name = command.GetElement(0).Name;
            if (name == "getMore" ? command["collection"] == NetDiagConstants.ObservationsCollection : command.GetElement(0).Value == NetDiagConstants.ObservationsCollection)
            {
                sent.Add(command);
            }
        }
        return sent;
    }

    [Test]
    public async Task Aggregate_GroupsSortsAndFinalizes()
    {
        var result = await _service.AggregateAsync(new NetDiagQuery { TargetKey = "flo:12" }, "asn,path");

        var body = (AggregateResponseDto)result.Body;
        Assert.IsFalse(result.Truncated);
        Assert.AreEqual(new[] { "asn", "path" }, body.GroupBy);
        Assert.AreEqual(2, body.Rows.Count);
        var dt = body.Rows[0];
        Assert.AreEqual(3320L, dt.Key["asn"]);
        Assert.AreEqual("direct", dt.Key["path"]);
        Assert.AreEqual(5, dt.Count);
        Assert.AreEqual(12.0, dt.P50MsMedian);
        Assert.AreEqual(14.0, dt.P50MsP90);
        Assert.AreEqual(0.0, dt.LossPctMedian);
        Assert.AreEqual(0.2, dt.ShareLossOver1Pct);
        Assert.AreEqual(0.0, dt.ShareUnreachable);
        Assert.AreEqual("Deutsche Telekom AG", dt.AsnOrg);
        Assert.AreEqual(52.0, dt.Lat);
        Assert.AreEqual(13.0, dt.Lon);
        Assert.AreEqual(1.0, body.Rows[1].ShareUnreachable);
        Assert.AreEqual(1, body.Rows[1].Count);
        Assert.AreEqual((-33.0, 151.0), (body.Rows[1].Lat, body.Rows[1].Lon));
    }

    // C6: lat and lon are the mean over the group's measurements. A measurement with many rows counts once.
    [Test]
    public async Task Aggregate_Centroid_IsTheMeanOverMeasurements_NotOverRows()
    {
        await _observations.InsertManyAsync(
        [
            Obs("big", asn: 7, path: "direct", p50: 1, loss: 0, lat: 0, lon: 0, run: "r2"),
            Obs("big", asn: 7, path: "direct", p50: 2, loss: 0, lat: 0, lon: 0, run: "r2"),
            Obs("big", asn: 7, path: "direct", p50: 3, loss: 0, lat: 0, lon: 0, run: "r2"),
            Obs("small", asn: 7, path: "direct", p50: 4, loss: 2, lat: 30, lon: 60, run: "r2"),
        ]);

        var body = (AggregateResponseDto)(await _service.AggregateAsync(new NetDiagQuery { RunId = ["r2"] }, "asn")).Body;

        var row = body.Rows.Single();
        Assert.AreEqual(4, row.Count);
        Assert.AreEqual((15.0, 30.0), (row.Lat, row.Lon));
        Assert.AreEqual(2.0, row.P50MsMedian);
        Assert.AreEqual(0.25, row.ShareLossOver1Pct);
    }

    [Test]
    public async Task Aggregate_GroupsByEveryC6Key_AndABooleanOrMissingKeyValue()
    {
        await _observations.InsertOneAsync(Obs("v", asn: 3320, path: "direct", p50: 1, loss: 0, lat: 1, lon: 1) with { VpnSuspected = true, City = "Berlin" });

        foreach (var key in AggregateBuilder.GroupKeys.Keys)
        {
            var result = await _service.AggregateAsync(new NetDiagQuery(), key);
            Assert.AreEqual(7, ((AggregateResponseDto)result.Body).Rows.Sum(r => r.Count), key);
        }
        var byVpn = (AggregateResponseDto)(await _service.AggregateAsync(new NetDiagQuery(), "vpn,city")).Body;
        Assert.AreEqual(new object[] { false, null, 6 }, new[] { byVpn.Rows[0].Key["vpn"], byVpn.Rows[0].Key["city"], byVpn.Rows[0].Count });
        Assert.AreEqual(new object[] { true, "Berlin", 1 }, new[] { byVpn.Rows[1].Key["vpn"], byVpn.Rows[1].Key["city"], byVpn.Rows[1].Count });
    }

    // P6: only the newest `scanLimit` matching rows are grouped, and at most `maxRows` groups come back, largest first.
    [Test]
    public async Task AggregateObservations_GroupsOnlyTheNewestRowsUpToTheScanLimit_AndCapsTheGroups()
    {
        await _observations.InsertManyAsync(
        [
            Obs("n1", asn: 1, path: "direct", p50: 1, loss: 0, lat: 1, lon: 1, run: "r3", minutesAfterNow: 5),
            Obs("n2", asn: 1, path: "direct", p50: 1, loss: 0, lat: 1, lon: 1, run: "r3", minutesAfterNow: 4),
            Obs("n3", asn: 2, path: "direct", p50: 1, loss: 0, lat: 1, lon: 1, run: "r3", minutesAfterNow: 3),
            Obs("old", asn: 3, path: "direct", p50: 1, loss: 0, lat: 1, lon: 1, run: "r3", minutesAfterNow: 1),
        ]);
        var filter = NetDiagFilters.Observations(new NetDiagQuery { RunId = ["r3"] });
        var stages = AggregateBuilder.GroupStages(["asn"]);

        var newestThree = await _repo.AggregateObservationsAsync(filter, stages, 3, 1000, CancellationToken.None);
        var largestGroup = await _repo.AggregateObservationsAsync(filter, stages, 1000, 1, CancellationToken.None);

        Assert.AreEqual(new[] { (1L, 2), (2L, 1) }, newestThree.Select(g => (g["_id"]["asn"].ToInt64(), g["count"].ToInt32())));
        Assert.AreEqual(new[] { (1L, 2) }, largestGroup.Select(g => (g["_id"]["asn"].ToInt64(), g["count"].ToInt32())));
        Assert.AreEqual(3, await _repo.CountObservationsAsync(filter, 3, CancellationToken.None), "counting stops at the limit");
        Assert.AreEqual(4, await _repo.CountObservationsAsync(filter, 1000, CancellationToken.None));
    }

    [Test]
    public async Task Aggregate_NoMatch_IsAnEmptyRowList()
    {
        var result = await _service.AggregateAsync(new NetDiagQuery { RunId = ["nope"] }, "asn");

        Assert.IsEmpty(((AggregateResponseDto)result.Body).Rows);
        Assert.IsFalse(result.Truncated);
    }

    // P6 and ruling a: match, newest first, row limit, then the groups; every command carries the time limit.
    [Test]
    public async Task EveryObservationRead_CarriesTheServerSideTimeLimit_AndTheAggregateLimitsBeforeItGroups()
    {
        var filter = NetDiagFilters.Observations(new NetDiagQuery { RunId = ["r1"] });
        Commands();

        await _repo.AggregateObservationsAsync(filter, AggregateBuilder.GroupStages(["asn"]), 200_000, 1000, CancellationToken.None);
        var aggregate = Commands().Single();
        await _repo.CountObservationsAsync(filter, 200_000, CancellationToken.None);
        await _repo.ObservationsAsync(filter, 3, CancellationToken.None);
        await _repo.ObservationPageEndAsync(filter, 3, CancellationToken.None);
        using (await _repo.OpenObservationsAsync(filter, 3, CancellationToken.None)) { }
        var others = Commands();

        Assert.AreEqual("aggregate", aggregate.GetElement(0).Name);
        Assert.AreEqual(
            new[] { "$match", "$sort", "$limit", "$group", "$group", "$sort", "$limit" },
            aggregate["pipeline"].AsBsonArray.Select(stage => stage.AsBsonDocument.GetElement(0).Name));
        Assert.AreEqual(new BsonDocument("SubmittedAt", -1), aggregate["pipeline"][1]["$sort"]);
        Assert.AreEqual(200_000, aggregate["pipeline"][2]["$limit"].ToInt32());
        Assert.AreEqual(1000, aggregate["pipeline"][6]["$limit"].ToInt32());
        Assert.IsTrue(aggregate["allowDiskUse"].ToBoolean());
        Assert.AreEqual(new[] { "aggregate", "find", "find", "find" }, others.Select(c => c.GetElement(0).Name));
        // A page that is buffered anyway comes in one round trip; a stream in batches.
        Assert.AreEqual(3, others[1]["batchSize"].ToInt32());
        Assert.AreEqual(500, others[3]["batchSize"].ToInt32());
        foreach (var command in others.Append(aggregate))
        {
            Assert.AreEqual((long)NetDiagConstants.AdminQueryMaxTime.TotalMilliseconds, command.GetValue("maxTimeMS", -1).ToInt64(), command.GetElement(0).Name);
        }
    }

    [TestCase(1)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(6)]
    [TestCase(5000)]
    public async Task Observations_CursorPagesThroughEverythingOnce(int limit)
    {
        var seen = new List<string>();
        var pages = 0;
        string cursor = null;
        do
        {
            var page = (ObservationPage)(await _service.GetObservationsAsync(new NetDiagQuery { RunId = ["r1"] }, cursor, limit)).Body;
            Assert.LessOrEqual(page.Items.Count, limit);
            seen.AddRange(page.Items.Select(o => o.MeasurementId));
            cursor = page.NextCursor;
            pages++;
        } while (cursor != null);

        Assert.AreEqual(Seeded, seen);
        Assert.AreEqual((Seeded.Length + limit - 1) / limit, pages, "the last page carries no cursor, also when it is full");
    }

    [TestCase(1)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(6)]
    [TestCase(5000)]
    public async Task ObservationStream_CursorPagesThroughEverythingOnce(int limit)
    {
        var seen = new List<string>();
        var pages = 0;
        string cursor = null;
        do
        {
            var stream = (ObservationStream)(await _service.OpenObservationStreamAsync(new NetDiagQuery { RunId = ["r1"] }, cursor, limit)).Body;
            using var rows = stream.Rows;
            var page = await rows.ToListAsync();
            Assert.LessOrEqual(page.Count, limit);
            seen.AddRange(page.Select(o => o.MeasurementId));
            cursor = stream.NextCursor;
            pages++;
        } while (cursor != null);

        Assert.AreEqual(Seeded, seen);
        Assert.AreEqual((Seeded.Length + limit - 1) / limit, pages);
    }

    private static readonly DateTime Base = new(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static NetworkDiagnosticObservation At(string m, int secondsAfterBase, string run) =>
        Obs(m, asn: 1, path: "direct", p50: 1, loss: 0, lat: 1, lon: 1, run: run) with { Id = ObjectId.GenerateNewId(Base.AddSeconds(secondsAfterBase)) };

    /// <summary>One page in either form: the JSON page or the streamed one.</summary>
    private async Task<(List<string> Rows, string NextCursor)> Page(bool streamed, NetDiagQuery query, string cursor, int limit)
    {
        if (!streamed)
        {
            var page = (ObservationPage)(await _service.GetObservationsAsync(query, cursor, limit)).Body;
            return (page.Items.Select(o => o.MeasurementId).ToList(), page.NextCursor);
        }
        var stream = (ObservationStream)(await _service.OpenObservationStreamAsync(query, cursor, limit)).Body;
        using var rows = stream.Rows;
        return ((await rows.ToListAsync()).Select(o => o.MeasurementId).ToList(), stream.NextCursor);
    }

    // An observation's _id is created when its result is built; the row is stored several round trips later. A
    // submit that built its ids first can therefore store them after a later submit's rows were already paged over.
    // Pages hold back ids younger than the settle margin, so such a row is not skipped.
    [TestCase(false)]
    [TestCase(true)]
    public async Task Observations_ARowStoredLate_WithAnOlderIdThanRowsAlreadyStored_IsNotSkipped(bool streamed)
    {
        var query = new NetDiagQuery { RunId = ["settle"] };
        await _observations.InsertManyAsync([At("old1", -100, "settle"), At("old2", -99, "settle"), At("old3", -98, "settle"), At("b", 10, "settle")]);
        var late = At("a-late", 5, "settle");

        // 20 s after Base: "b" (10 s old) and the still unstored "a-late" (15 s old) are inside the margin.
        _time.Now = Base.AddSeconds(20);
        var first = await Page(streamed, query, null, 2);
        var second = await Page(streamed, query, first.NextCursor, 2);
        await _observations.InsertOneAsync(late);
        // Later, with the cursor obtained before the late row existed.
        _time.Now = Base.AddSeconds(60);
        var afterwards = await Page(streamed, query, first.NextCursor, 10);

        Assert.AreEqual(new[] { "old1", "old2" }, first.Rows);
        Assert.IsNotNull(first.NextCursor);
        Assert.AreEqual(new[] { "old3" }, second.Rows, "b is not settled yet: paging past it would skip a-late");
        Assert.IsNull(second.NextCursor, "the settle boundary ended the page: no cursor, no polling loop");
        Assert.AreEqual(new[] { "old3", "a-late", "b" }, afterwards.Rows);
        Assert.IsNull(afterwards.NextCursor);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Observations_OnlyRowsOlderThanTheSettleMargin_AreReturned(bool streamed)
    {
        var query = new NetDiagQuery { RunId = ["margin"] };
        await _observations.InsertManyAsync([At("31s", -31, "margin"), At("30s", -30, "margin"), At("29s", -29, "margin"), At("now", 0, "margin")]);
        _time.Now = Base;

        var all = await Page(streamed, query, null, 10);
        var exactlyFull = await Page(streamed, query, null, 1);
        _time.Now = Base.AddSeconds(1);
        var oneSecondLater = await Page(streamed, query, null, 1);

        // A row whose id is exactly one margin old is not settled yet (the boundary is exclusive).
        Assert.AreEqual(new[] { "31s" }, all.Rows);
        Assert.IsNull(all.NextCursor);
        Assert.AreEqual(new[] { "31s" }, exactlyFull.Rows);
        Assert.IsNull(exactlyFull.NextCursor, "the only row after it is not settled: the boundary ended the page, not the limit");
        Assert.AreEqual(new[] { "31s" }, oneSecondLater.Rows);
        Assert.IsNotNull(oneSecondLater.NextCursor, "now a settled row follows");
    }

    [Test]
    public async Task Observations_NothingSettledYet_IsAnEmptyLastPage()
    {
        _time.Now = DateTimeOffset.UtcNow.AddSeconds(-5);

        var page = (ObservationPage)(await _service.GetObservationsAsync(new NetDiagQuery { RunId = ["r1"] }, null, 4)).Body;

        Assert.IsEmpty(page.Items);
        Assert.IsNull(page.NextCursor);
    }

    // The streamed page announces its last row as the cursor before it reads the rows. A row that turns up inside the
    // page after that lookup makes the page a row longer; the announced row is never cut off.
    [Test]
    public async Task OpenObservations_UpToAPageEnd_ReturnsEveryRowOfTheRange_AlsoOneStoredAfterTheLookup()
    {
        await _observations.InsertManyAsync([At("p1", 1, "range"), At("p3", 3, "range"), At("p5", 5, "range")]);
        var filter = NetDiagFilters.Observations(new NetDiagQuery { RunId = ["range"] });
        var pageEnd = await _repo.ObservationPageEndAsync(filter, 2, CancellationToken.None);
        await _observations.InsertOneAsync(At("p2-late", 2, "range"));

        using var cursor = await _repo.OpenObservationsAsync(NetDiagFilters.IdRange(filter, null, pageEnd, null), null, CancellationToken.None);
        var rows = await cursor.ToListAsync();

        Assert.AreEqual(new[] { "p1", "p2-late", "p3" }, rows.Select(o => o.MeasurementId));
        Assert.AreEqual(pageEnd, rows[^1].Id, "the row the cursor names is part of the page");
    }

    // One measurement without an ASN name does not blank the name of its group.
    [Test]
    public async Task Aggregate_AsnOrg_IsTakenFromAnyMeasurementThatHasOne()
    {
        await _observations.InsertManyAsync(
        [
            Obs("n1", asn: 9, path: "direct", p50: 1, loss: 0, lat: 1, lon: 1, run: "org") with { AsnOrg = null },
            Obs("n2", asn: 9, path: "direct", p50: 1, loss: 0, lat: 1, lon: 1, run: "org") with { AsnOrg = "Named" },
            Obs("n3", asn: 9, path: "direct", p50: 1, loss: 0, lat: 1, lon: 1, run: "org") with { AsnOrg = null },
            Obs("n4", asn: 8, path: "direct", p50: 1, loss: 0, lat: 1, lon: 1, run: "org") with { AsnOrg = null },
        ]);

        var rows = ((AggregateResponseDto)(await _service.AggregateAsync(new NetDiagQuery { RunId = ["org"] }, "asn")).Body).Rows;

        Assert.AreEqual(new object[] { 9L, 3, "Named" }, new object[] { rows[0].Key["asn"], rows[0].Count, rows[0].AsnOrg });
        Assert.AreEqual(new object[] { 8L, 1, null }, new object[] { rows[1].Key["asn"], rows[1].Count, rows[1].AsnOrg });
    }

    // A cursor is only a position: a well-formed one that names no row neither fails nor widens the query.
    [Test]
    public async Task Observations_WellFormedCursorOfNoRow_IsJustAPosition()
    {
        var beyond = (ObservationPage)(await _service.GetObservationsAsync(new NetDiagQuery(), "ffffffffffffffffffffffff", 10)).Body;
        var before = (ObservationPage)(await _service.GetObservationsAsync(new NetDiagQuery { RunId = ["nope"] }, "000000000000000000000000", 10)).Body;
        var tampered = await _service.GetObservationsAsync(new NetDiagQuery(), "{\"$ne\":null}", 10);

        Assert.IsEmpty(beyond.Items);
        Assert.IsNull(beyond.NextCursor);
        Assert.IsEmpty(before.Items, "the cursor is added to the filters, it does not replace them");
        Assert.AreEqual(400, tampered.StatusCode);
    }

    [Test]
    public async Task Observations_ApplyTheSharedFilters()
    {
        var page = (ObservationPage)(await _service.GetObservationsAsync(new NetDiagQuery { NodeId = 12, Asn = 1221, From = Now, To = Now.AddSeconds(1) }, null, 10)).Body;
        var none = (ObservationPage)(await _service.GetObservationsAsync(new NetDiagQuery { NodeId = 12, To = Now }, null, 10)).Body;

        Assert.AreEqual(new[] { "m9" }, page.Items.Select(o => o.MeasurementId));
        Assert.AreEqual(DateTimeKind.Utc, page.Items[0].SubmittedAt.Kind);
        Assert.IsEmpty(none.Items, "to is exclusive (A5)");
    }

    [Test]
    public async Task ObservationReads_HonourTheIdRange_AndThePageEndIsTheLastRowOfAFullPageWithAFollower()
    {
        var filter = NetDiagFilters.Observations(new NetDiagQuery { RunId = ["r1"] });
        var ids = (await _repo.ObservationsAsync(filter, 10, CancellationToken.None)).Select(o => o.Id).ToList();
        FilterDefinition<NetworkDiagnosticObservation> Range(ObjectId? after = null, ObjectId? upTo = null, ObjectId? before = null) =>
            NetDiagFilters.IdRange(filter, after, upTo, before);

        using var bounded = await _repo.OpenObservationsAsync(Range(ids[0], ids[2]), 10, CancellationToken.None);
        var rows = await bounded.ToListAsync();
        var settled = await _repo.ObservationsAsync(Range(before: ids[2]), 10, CancellationToken.None);

        Assert.AreEqual(new[] { "m1", "m2" }, rows.Select(o => o.MeasurementId));
        Assert.AreEqual(new[] { "m0", "m1" }, settled.Select(o => o.MeasurementId), "before is exclusive");
        Assert.AreEqual(ids[1], await _repo.ObservationPageEndAsync(filter, 2, CancellationToken.None));
        Assert.AreEqual(ids[4], await _repo.ObservationPageEndAsync(Range(ids[2]), 2, CancellationToken.None));
        Assert.IsNull(await _repo.ObservationPageEndAsync(Range(ids[3]), 2, CancellationToken.None), "exactly a full last page");
        Assert.IsNull(await _repo.ObservationPageEndAsync(Range(before: ids[2]), 2, CancellationToken.None), "the row after the page is not settled");
        Assert.IsNull(await _repo.ObservationPageEndAsync(filter, 10, CancellationToken.None));
    }

    // The export is read in batches from the server while it is written out, not loaded at once.
    [Test]
    public async Task OpenObservations_ReadsALargeResultInBatches()
    {
        var many = Enumerable.Range(0, 1200).Select(i => Obs($"x{i}", asn: 1, path: "direct", p50: 1, loss: 0, lat: 1, lon: 1, run: "big")).ToList();
        await _observations.InsertManyAsync(many);
        var filter = NetDiagFilters.Observations(new NetDiagQuery { RunId = ["big"] });
        Commands();

        using var cursor = await _repo.OpenObservationsAsync(filter, 5000, CancellationToken.None);
        var batches = new List<int>();
        while (await cursor.MoveNextAsync())
        {
            batches.Add(cursor.Current.Count());
        }

        Assert.AreEqual(1200, batches.Sum());
        Assert.Greater(batches.Count, 1);
        Assert.LessOrEqual(batches.Max(), 500);
        CollectionAssert.Contains(Commands().Select(c => c.GetElement(0).Name).ToList(), "getMore");
    }

    [Test]
    public void OpenObservations_CancelledToken_Throws()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.That(
            async () => await _repo.OpenObservationsAsync(NetDiagFilters.Observations(new NetDiagQuery()), 10, cancelled.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }
}
