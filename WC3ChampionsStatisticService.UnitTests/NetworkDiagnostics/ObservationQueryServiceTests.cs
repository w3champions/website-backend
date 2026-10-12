using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using NUnit.Framework;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.NetworkDiagnostics;
using W3ChampionsStatisticService.NetworkDiagnostics.Api;
using W3ChampionsStatisticService.NetworkDiagnostics.Persistence;
using W3ChampionsStatisticService.NetworkDiagnostics.Results;

namespace WC3ChampionsStatisticService.Tests.NetworkDiagnostics;

/// <summary>The observation and aggregate members of the query service (C6), against a strict repository mock.</summary>
[TestFixture]
public class ObservationQueryServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);
    /// <summary>Rows with a smaller _id are settled at <see cref="Now"/>: every page ends before it.</summary>
    private static readonly ObjectId Settled = NetworkDiagnosticsQueryService.SettledBoundary(Now);

    private Mock<INetworkDiagnosticsResultRepository> _results;
    private NetworkDiagnosticsQueryService _service;

    [SetUp]
    public void SetUp()
    {
        // Strict: a request answered with 400 must not have run any query.
        _results = new Mock<INetworkDiagnosticsResultRepository>(MockBehavior.Strict);
        _service = new NetworkDiagnosticsQueryService(_results.Object, new FixedTimeProvider(Now));
    }

    private static BsonDocument IdRange(ObjectId? after = null, ObjectId? upTo = null)
    {
        var range = new BsonDocument();
        if (after is { } lower) range.Add("$gt", lower);
        if (upTo is { } upper) range.Add("$lte", upper);
        range.Add("$lt", Settled);
        return range;
    }

    // Ids are created before their rows are stored, so a row can appear behind newer ones: only ids older than the
    // settle margin are paged over.
    [Test]
    public void SettledBoundary_IsTheSmallestIdOfTheSecondOneSettleMarginAgo()
    {
        var boundary = NetworkDiagnosticsQueryService.SettledBoundary(new DateTimeOffset(Now));

        Assert.AreEqual(TimeSpan.FromSeconds(30), NetDiagConstants.ObservationSettleMargin);
        Assert.AreEqual(Now.AddSeconds(-30), boundary.CreationTime);
        Assert.AreEqual(new byte[8], boundary.ToByteArray()[4..]);
        Assert.That(boundary, Is.LessThan(ObjectId.GenerateNewId(Now.AddSeconds(-30))));
        Assert.That(boundary, Is.GreaterThan(ObjectId.GenerateNewId(Now.AddSeconds(-31))));
    }

    private static NetworkDiagnosticObservation Obs(string measurementId) =>
        new() { Id = ObjectId.GenerateNewId(), MeasurementId = measurementId, RunId = "r1", TargetKey = "flo:12" };

    private static string ErrorOf(NetDiagResult result) => JsonSerializer.SerializeToElement(result.Body).GetProperty("error").GetString();

    private void PageReturns(int fetched, List<NetworkDiagnosticObservation> rows, Action<BsonDocument> seeFilter = null) =>
        _results.Setup(r => r.ObservationsAsync(It.IsAny<FilterDefinition<NetworkDiagnosticObservation>>(), fetched, It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<NetworkDiagnosticObservation>, int, CancellationToken>((f, _, _) => seeFilter?.Invoke(FilterRender.Render(f)))
            .ReturnsAsync(rows);

    // P8 for the cursor; the limit follows the pageSize precedent (a parsed number outside its range names the range).
    [TestCase("not-an-objectid", 100, "invalid query parameter: cursor")]
    [TestCase("zzzzzzzzzzzzzzzzzzzzzzzz", 100, "invalid query parameter: cursor")]
    [TestCase("507f1f77bcf86cd79943901", 100, "invalid query parameter: cursor")]
    [TestCase("{\"$gt\":\"\"}", 100, "invalid query parameter: cursor")]
    [TestCase(null, 0, "limit must be in 1-5000")]
    [TestCase(null, -1, "limit must be in 1-5000")]
    [TestCase(null, 5001, "limit must be in 1-5000")]
    [TestCase(null, int.MaxValue, "limit must be in 1-5000")]
    public async Task Observations_BadCursorOrLimit_400_BeforeAnyQuery(string cursor, int limit, string error)
    {
        var page = await _service.GetObservationsAsync(new NetDiagQuery(), cursor, limit);
        var stream = await _service.OpenObservationStreamAsync(new NetDiagQuery(), cursor, limit);

        Assert.AreEqual((400, error), (page.StatusCode, ErrorOf(page)));
        Assert.AreEqual((400, error), (stream.StatusCode, ErrorOf(stream)));
        _results.VerifyNoOtherCalls();
    }

    // The shared filter rules of the measurement list apply here too: an unknown path or method can match nothing.
    [TestCase("Direct", null, "path")]
    [TestCase("relay", null, "path")]
    [TestCase(null, "tcpping", "method")]
    public async Task UnknownPathOrMethod_400NamingTheParameter_BeforeAnyQuery(string path, string method, string key)
    {
        var query = new NetDiagQuery { Path = path, Method = method };

        var answers = new[]
        {
            await _service.GetObservationsAsync(query, null, 100),
            await _service.OpenObservationStreamAsync(query, null, 100),
            await _service.AggregateAsync(query, "asn"),
        };

        foreach (var answer in answers)
        {
            Assert.AreEqual((400, $"invalid query parameter: {key}"), (answer.StatusCode, ErrorOf(answer)));
        }
        _results.VerifyNoOtherCalls();
    }

    [Test]
    public async Task GetObservations_FetchesOneRowMoreThanAsked_AndTheCursorIsTheLastRowOfThePage()
    {
        var rows = new List<NetworkDiagnosticObservation> { Obs("m0"), Obs("m1"), Obs("m2") };
        BsonDocument filter = null;
        PageReturns(3, rows, f => filter = f);

        var result = await _service.GetObservationsAsync(new NetDiagQuery { NodeId = 12, BattleTag = "Name#1234" }, null, 2);

        var page = (ObservationPage)result.Body;
        Assert.AreEqual(200, result.StatusCode);
        Assert.AreEqual(new[] { "m0", "m1" }, page.Items.Select(o => o.MeasurementId));
        Assert.AreEqual(rows[1].Id.ToString(), page.NextCursor);
        Assert.IsFalse(result.Truncated);
        // The shared filter builder (nodeId is asked through the target key), and only settled rows.
        Assert.AreEqual(new BsonDocument { { "BattleTag", "Name#1234" }, { "TargetKey", "flo:12" }, { "_id", IdRange() } }, filter);
        StringAssert.StartsWith("""{"items":[{"measurementId":"m0",""", JsonSerializer.Serialize(page, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    // Fewer rows than asked: the settle boundary (or the end of the data) ended the page, not the limit. No cursor is
    // announced, so a client is not sent polling for rows that are not settled yet.
    [Test]
    public async Task GetObservations_Cursor_ContinuesAfterThatRow_AndAPageNotEndedByTheLimitHasNoCursor()
    {
        var cursor = ObjectId.GenerateNewId();
        BsonDocument filter = null;
        PageReturns(3, [Obs("m2"), Obs("m3")], f => filter = f);

        var page = (ObservationPage)(await _service.GetObservationsAsync(new NetDiagQuery(), cursor.ToString(), 2)).Body;

        Assert.AreEqual(new[] { "m2", "m3" }, page.Items.Select(o => o.MeasurementId));
        Assert.IsNull(page.NextCursor);
        Assert.AreEqual(new BsonDocument("_id", IdRange(after: cursor)), filter);
        StringAssert.EndsWith("""],"nextCursor":null}""", JsonSerializer.Serialize(page, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Test]
    public async Task GetObservations_NullQueryAndTheLargestLimit_AreAccepted()
    {
        PageReturns(NetworkDiagnosticsQueryService.MaxObservationLimit + 1, []);

        var result = await _service.GetObservationsAsync(null, "", NetworkDiagnosticsQueryService.MaxObservationLimit);

        Assert.AreEqual(200, result.StatusCode);
        Assert.IsEmpty(((ObservationPage)result.Body).Items);
    }

    // The next cursor travels in a response header, so it is known before the first row is read: the page end is
    // looked up first. The stream then runs to that row inclusive and WITHOUT a row limit: a row that turns up inside
    // the range after the lookup makes the page one row longer instead of cutting off the row the cursor names.
    [Test]
    public async Task OpenObservationStream_MoreRowsExist_StreamsUpToThePageEnd_WithoutARowLimit_AndItIsTheNextCursor()
    {
        var after = ObjectId.GenerateNewId();
        var pageEnd = ObjectId.GenerateNewId();
        var rows = new FakeCursor<NetworkDiagnosticObservation>();
        BsonDocument probed = null, opened = null;
        _results.Setup(r => r.ObservationPageEndAsync(It.IsAny<FilterDefinition<NetworkDiagnosticObservation>>(), 50, It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<NetworkDiagnosticObservation>, int, CancellationToken>((f, _, _) => probed = FilterRender.Render(f))
            .ReturnsAsync(pageEnd);
        _results.Setup(r => r.OpenObservationsAsync(It.IsAny<FilterDefinition<NetworkDiagnosticObservation>>(), null, It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<NetworkDiagnosticObservation>, int?, CancellationToken>((f, _, _) => opened = FilterRender.Render(f))
            .ReturnsAsync(rows);

        var result = await _service.OpenObservationStreamAsync(new NetDiagQuery { RunId = ["r1"] }, after.ToString(), 50);

        var stream = (ObservationStream)result.Body;
        Assert.AreEqual(200, result.StatusCode);
        Assert.AreSame(rows, stream.Rows);
        Assert.AreEqual(pageEnd.ToString(), stream.NextCursor);
        var runs = new BsonDocument("$in", new BsonArray { "r1" });
        Assert.AreEqual(new BsonDocument { { "RunId", runs }, { "_id", IdRange(after: after) } }, probed);
        Assert.AreEqual(new BsonDocument { { "RunId", runs }, { "_id", IdRange(after: after, upTo: pageEnd) } }, opened);
    }

    [Test]
    public async Task OpenObservationStream_NoFullPageOfSettledRows_StreamsAtMostTheLimit_AndHasNoCursor()
    {
        var rows = new FakeCursor<NetworkDiagnosticObservation>();
        BsonDocument opened = null;
        _results.Setup(r => r.ObservationPageEndAsync(It.IsAny<FilterDefinition<NetworkDiagnosticObservation>>(), 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ObjectId?)null);
        _results.Setup(r => r.OpenObservationsAsync(It.IsAny<FilterDefinition<NetworkDiagnosticObservation>>(), 50, It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<NetworkDiagnosticObservation>, int?, CancellationToken>((f, _, _) => opened = FilterRender.Render(f))
            .ReturnsAsync(rows);

        var stream = (ObservationStream)(await _service.OpenObservationStreamAsync(new NetDiagQuery(), null, 50)).Body;

        Assert.AreSame(rows, stream.Rows);
        Assert.IsNull(stream.NextCursor);
        Assert.AreEqual(new BsonDocument("_id", IdRange()), opened);
    }

    [Test]
    public async Task Observations_PassTheRequestsCancellationToken()
    {
        using var cancelled = new CancellationTokenSource();
        var token = cancelled.Token;
        _results.Setup(r => r.ObservationsAsync(It.IsAny<FilterDefinition<NetworkDiagnosticObservation>>(), 2, token)).ReturnsAsync([]);
        _results.Setup(r => r.ObservationPageEndAsync(It.IsAny<FilterDefinition<NetworkDiagnosticObservation>>(), 1, token)).ReturnsAsync((ObjectId?)null);
        _results.Setup(r => r.OpenObservationsAsync(It.IsAny<FilterDefinition<NetworkDiagnosticObservation>>(), 1, token))
            .ReturnsAsync(new FakeCursor<NetworkDiagnosticObservation>());
        _results.Setup(r => r.AggregateObservationsAsync(
                It.IsAny<FilterDefinition<NetworkDiagnosticObservation>>(), It.IsAny<IReadOnlyList<BsonDocument>>(), It.IsAny<int>(), It.IsAny<int>(), token))
            .ReturnsAsync([]);

        await _service.GetObservationsAsync(new NetDiagQuery(), null, 1, token);
        await _service.OpenObservationStreamAsync(new NetDiagQuery(), null, 1, token);
        await _service.AggregateAsync(new NetDiagQuery(), "asn", token);

        _results.VerifyAll();
    }

    // P8.
    [TestCase(null)]
    [TestCase("")]
    [TestCase("battleTag")]
    [TestCase("asn,path,method,os")]
    public async Task Aggregate_BadGroupBy_400_BeforeAnyQuery(string groupBy)
    {
        var result = await _service.AggregateAsync(new NetDiagQuery(), groupBy);

        Assert.AreEqual((400, "invalid query parameter: groupBy"), (result.StatusCode, ErrorOf(result)));
        _results.VerifyNoOtherCalls();
    }

    private static BsonDocument Group(string path, int count) => new()
    {
        { "_id", new BsonDocument("path", path) },
        { "count", count },
        { "p50s", new BsonArray { new BsonArray { 10.0 } } },
        { "losses", new BsonArray { new BsonArray { 0.0 } } },
        { "lossOver1", 0 },
        { "unreachable", 0 },
    };

    private void AggregateReturns(List<BsonDocument> groups, Action<BsonDocument, IReadOnlyList<BsonDocument>> see = null) =>
        _results.Setup(r => r.AggregateObservationsAsync(
                It.IsAny<FilterDefinition<NetworkDiagnosticObservation>>(), It.IsAny<IReadOnlyList<BsonDocument>>(),
                NetDiagConstants.MaxScannedObservations, AggregateBuilder.MaxRows, It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<NetworkDiagnosticObservation>, IReadOnlyList<BsonDocument>, int, int, CancellationToken>(
                (f, stages, _, _, _) => see?.Invoke(FilterRender.Render(f), stages))
            .ReturnsAsync(groups);

    // P6: at most the newest 200 000 matching rows are grouped, at most 1000 groups are answered.
    [Test]
    public async Task Aggregate_IsBounded_AndAnswersTheC6Shape()
    {
        BsonDocument filter = null;
        IReadOnlyList<BsonDocument> stages = null;
        AggregateReturns([Group("direct", 3), Group("proxied", 1)], (f, s) => (filter, stages) = (f, s));

        var result = await _service.AggregateAsync(new NetDiagQuery { TargetKey = "flo:12" }, " path ");

        var body = (AggregateResponseDto)result.Body;
        Assert.AreEqual(200, result.StatusCode);
        Assert.IsFalse(result.Truncated);
        Assert.AreEqual(new[] { "path" }, body.GroupBy);
        Assert.AreEqual(new[] { 3, 1 }, body.Rows.Select(r => r.Count));
        Assert.AreEqual(new BsonDocument("TargetKey", "flo:12"), filter);
        Assert.AreEqual(AggregateBuilder.GroupStages(["path"]), stages);
        StringAssert.StartsWith(
            """{"groupBy":["path"],"rows":[{"key":{"path":"direct"},"count":3,"p50MsMedian":10,"p50MsP90":10,"lossPctMedian":0,"shareLossOver1Pct":0,"shareUnreachable":0,"asnOrg":null,"lat":null,"lon":null},""",
            JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    // The groups that came back hold every grouped row, so their counts tell whether the row limit was reached.
    [Test]
    public async Task Aggregate_GroupedRowsReachTheLimit_IsFlaggedTruncated()
    {
        AggregateReturns([Group("direct", NetDiagConstants.MaxScannedObservations - 1), Group("proxied", 1)]);

        var result = await _service.AggregateAsync(new NetDiagQuery(), "path");

        Assert.IsTrue(result.Truncated);
    }

    [Test]
    public async Task Aggregate_GroupedRowsBelowTheLimit_IsNotTruncated_AndNeedsNoCount()
    {
        AggregateReturns([Group("direct", NetDiagConstants.MaxScannedObservations - 2), Group("proxied", 1)]);

        var result = await _service.AggregateAsync(new NetDiagQuery(), "path");

        Assert.IsFalse(result.Truncated);
        _results.Verify(r => r.CountObservationsAsync(It.IsAny<FilterDefinition<NetworkDiagnosticObservation>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // With the full 1000 groups answered, groups were cut off, and the answered counts no longer add up to the rows
    // that were grouped: the matching rows are counted (up to the limit) instead.
    [TestCase(NetDiagConstants.MaxScannedObservations, true)]
    [TestCase(NetDiagConstants.MaxScannedObservations - 1, false)]
    public async Task Aggregate_GroupListIsFull_CountsTheMatchingRowsToTellWhetherTheLimitWasReached(int matching, bool truncated)
    {
        BsonDocument counted = null;
        AggregateReturns(Enumerable.Range(0, AggregateBuilder.MaxRows).Select(i => Group($"p{i}", 1)).ToList());
        _results.Setup(r => r.CountObservationsAsync(It.IsAny<FilterDefinition<NetworkDiagnosticObservation>>(), NetDiagConstants.MaxScannedObservations, It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<NetworkDiagnosticObservation>, int, CancellationToken>((f, _, _) => counted = FilterRender.Render(f))
            .ReturnsAsync((long)matching);

        var result = await _service.AggregateAsync(new NetDiagQuery { Os = "windows" }, "runId");

        Assert.AreEqual(truncated, result.Truncated);
        Assert.AreEqual(AggregateBuilder.MaxRows, ((AggregateResponseDto)result.Body).Rows.Count);
        Assert.AreEqual(new BsonDocument("Os", "windows"), counted);
    }

    // The filter set names a player and IPs: it stays out of trace tags. The tracing proxy reads the attribute from
    // the interface method.
    [TestCase(nameof(INetworkDiagnosticsQueryService.GetObservationsAsync))]
    [TestCase(nameof(INetworkDiagnosticsQueryService.OpenObservationStreamAsync))]
    [TestCase(nameof(INetworkDiagnosticsQueryService.AggregateAsync))]
    public void QueryParameter_IsNoTraceOnTheInterface(string method)
    {
        var parameter = typeof(INetworkDiagnosticsQueryService).GetMethod(method)!.GetParameters().Single(p => p.ParameterType == typeof(NetDiagQuery));

        Assert.IsNotNull(parameter.GetCustomAttribute<NoTraceAttribute>());
    }

    [TestCase(nameof(INetworkDiagnosticsResultRepository.ObservationsAsync))]
    [TestCase(nameof(INetworkDiagnosticsResultRepository.ObservationPageEndAsync))]
    [TestCase(nameof(INetworkDiagnosticsResultRepository.OpenObservationsAsync))]
    [TestCase(nameof(INetworkDiagnosticsResultRepository.AggregateObservationsAsync))]
    [TestCase(nameof(INetworkDiagnosticsResultRepository.CountObservationsAsync))]
    public void RepositoryFilterParameter_IsNoTraceOnTheInterface(string method)
    {
        var parameter = typeof(INetworkDiagnosticsResultRepository).GetMethod(method)!.GetParameters().Single(p => p.Name == "filter");

        Assert.IsNotNull(parameter.GetCustomAttribute<NoTraceAttribute>());
    }

    // A10 / Review Focus 7: nothing these endpoints answer has a token-like property.
    [TestCase(typeof(ObservationDto))]
    [TestCase(typeof(ObservationPage))]
    [TestCase(typeof(AggregateRowDto))]
    [TestCase(typeof(AggregateResponseDto))]
    public void AnswerTypes_HaveNoTokenProperty(Type type)
    {
        foreach (var property in type.GetProperties())
        {
            StringAssert.DoesNotContain("token", property.Name.ToLowerInvariant(), $"{type.Name}.{property.Name}");
        }
    }
}
