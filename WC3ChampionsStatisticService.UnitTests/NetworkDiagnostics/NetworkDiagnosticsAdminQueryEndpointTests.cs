using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using NUnit.Framework;
using W3ChampionsStatisticService.NetworkDiagnostics.Api;
using W3ChampionsStatisticService.NetworkDiagnostics.Results;
using W3ChampionsStatisticService.NetworkDiagnostics.Runs;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace WC3ChampionsStatisticService.Tests.NetworkDiagnostics;

/// <summary>
/// The C6 measurement endpoints over real HTTP: routing, MVC query binding of the shared filter set and JSON output.
/// The permission filter (it needs a signed admin JWT) is replaced by a stand-in that does the one thing that
/// matters for binding: it overwrites the "battleTag" action argument with the acting admin. The requests are the
/// ones the website builds (NetworkDiagnosticsService.ts, measurementFilterParams).
/// </summary>
[TestFixture]
public class NetworkDiagnosticsAdminQueryEndpointTests
{
    private const string ActingAdmin = "Admin#1";

    private WebApplication _app;
    private HttpClient _http;
    private Mock<INetworkDiagnosticsQueryService> _queries;
    private Mock<INetworkDiagnosticsRunService> _runService;
    private NetDiagQuery _seenQuery;
    private (int Page, int PageSize) _seenPaging;

    [OneTimeSetUp]
    public async Task StartServer()
    {
        _queries = new Mock<INetworkDiagnosticsQueryService>();

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddControllers(options => options.Conventions.Add(new ActingAdminStandIn()))
            .AddApplicationPart(typeof(NetworkDiagnosticsAdminController).Assembly)
            .ConfigureApplicationPartManager(parts =>
            {
                parts.FeatureProviders.Clear();
                parts.FeatureProviders.Add(new OnlyTheAdminController());
            });
        _runService = new Mock<INetworkDiagnosticsRunService>();
        builder.Services.AddSingleton(_runService.Object);
        builder.Services.AddSingleton(_queries.Object);

        _app = builder.Build();
        _app.UseRouting();
        _app.MapControllers();
        await _app.StartAsync();
        _http = new HttpClient { BaseAddress = new Uri(_app.Urls.Single()) };
    }

    [OneTimeTearDown]
    public async Task StopServer()
    {
        _http?.Dispose();
        if (_app != null) await _app.DisposeAsync();
    }

    [SetUp]
    public void SetUp()
    {
        _queries.Reset();
        _runService.Reset();
        _seenQuery = null;
        _seenPaging = default;
        _queries.Setup(q => q.ListMeasurementsAsync(It.IsAny<NetDiagQuery>(), It.IsAny<int>(), It.IsAny<int>()))
            .Callback<NetDiagQuery, int, int>((query, page, pageSize) => (_seenQuery, _seenPaging) = (query, (page, pageSize)))
            .ReturnsAsync(NetDiagResult.Ok(new PageDto<MeasurementSummaryDto>([], 0)));
    }

    private async Task<(int Status, string Body, HttpResponseMessage Response)> Get(string pathAndQuery)
    {
        var response = await _http.GetAsync(pathAndQuery);
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync(), response);
    }

    [Test]
    public async Task ListMeasurements_BindsEverySharedFilter_AsTheWebsiteSendsThem()
    {
        var (status, body, _) = await Get(
            "/api/network-diagnostics/measurements?battleTag=Name%231234&runId=r1&runId=r2&measurementId=m1&measurementId=m2"
            + "&targetKey=flo%3A12&nodeId=12&targetIp=1.2.3.4&path=direct&method=IcmpMtr&country=DE&city=Berlin&asn=3320"
            + "&os=windows&adapterType=wifi&vpn=false&from=2026-10-01T00%3A00%3A00.000Z&to=2026-10-02T00%3A00%3A00.000Z&page=2&pageSize=50");

        Assert.AreEqual((200, """{"items":[],"total":0}"""), (status, body));
        // The filtered player is the one in the query string, not the acting admin the permission filter injects.
        Assert.AreEqual("Name#1234", _seenQuery.BattleTag);
        Assert.AreEqual(new[] { "r1", "r2" }, _seenQuery.RunId);
        Assert.AreEqual(new[] { "m1", "m2" }, _seenQuery.MeasurementId);
        Assert.AreEqual(("flo:12", 12, "1.2.3.4", "direct", "IcmpMtr"), (_seenQuery.TargetKey, _seenQuery.NodeId!.Value, _seenQuery.TargetIp, _seenQuery.Path, _seenQuery.Method));
        Assert.AreEqual(("DE", "Berlin", 3320L, "windows", "wifi", false), (_seenQuery.Country, _seenQuery.City, _seenQuery.Asn!.Value, _seenQuery.Os, _seenQuery.AdapterType, _seenQuery.Vpn!.Value));
        Assert.AreEqual((2, 50), _seenPaging);

        // A5: the instants are the UTC ones that were sent, whatever the server's time zone.
        var window = FilterRender.Render(NetDiagFilters.Measurements(_seenQuery, []))["SubmittedAt"];
        Assert.AreEqual(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), window["$gte"].ToUniversalTime());
        Assert.AreEqual(new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc), window["$lt"].ToUniversalTime());
    }

    // The body of POST runs is read by the action, whatever the content type says (api.md: "content type is not
    // checked"). For a form content type MVC's form value provider would otherwise consume the body first and the
    // action would see an empty one.
    [TestCase("application/json")]
    [TestCase("application/x-www-form-urlencoded")]
    [TestCase("multipart/form-data; boundary=x")]
    [TestCase("text/plain")]
    public async Task CreateRun_TheJsonBodyIsParsed_WhateverTheContentType(string contentType)
    {
        CreateRunRequest seen = null;
        string seenAdmin = null;
        _runService.Setup(s => s.CreateRunAsync(It.IsAny<CreateRunRequest>(), It.IsAny<string>()))
            .Callback<CreateRunRequest, string>((request, admin) => (seen, seenAdmin) = (request, admin))
            .ReturnsAsync(new NetDiagResult(201, new { id = "r1" }));
        using var content = new ByteArrayContent(
            """{"audience":{"kind":"SampleCount","count":5},"targets":{"floNodeIds":[12]},"methods":["IcmpMtr"],"windowSeconds":90}"""u8.ToArray());
        content.Headers.TryAddWithoutValidation("Content-Type", contentType);

        using var response = await _http.PostAsync("/api/network-diagnostics/runs", content);

        Assert.AreEqual((201, """{"id":"r1"}"""), ((int)response.StatusCode, await response.Content.ReadAsStringAsync()));
        Assert.IsNotNull(seen, "the action saw an empty body");
        Assert.AreEqual((AudienceKind.SampleCount, 5, 90), (seen.Audience.Kind, seen.Audience.Count!.Value, seen.WindowSeconds!.Value));
        Assert.AreEqual(new[] { 12 }, seen.Targets.FloNodeIds);
        Assert.AreEqual(ActingAdmin, seenAdmin);
    }

    [Test]
    public async Task ListMeasurements_NoQuery_IsAnEmptyFilterOnTheFirstPage()
    {
        var (status, _, response) = await Get("/api/network-diagnostics/measurements");

        Assert.AreEqual(200, status);
        Assert.AreEqual(new NetDiagQuery(), _seenQuery with { RunId = null, MeasurementId = null });
        Assert.That(_seenQuery.RunId, Is.Null.Or.Empty);
        Assert.That(_seenQuery.MeasurementId, Is.Null.Or.Empty);
        Assert.AreEqual((0, 25), _seenPaging);
        Assert.IsFalse(response.Headers.Contains("X-NetDiag-Truncated"));
    }

    // An offset-less instant is UTC (A5), and vpn=true binds.
    [Test]
    public async Task ListMeasurements_InstantWithoutOffset_IsTakenAsUtc()
    {
        await Get("/api/network-diagnostics/measurements?from=2026-10-01T00:00:00&vpn=true");

        Assert.AreEqual(true, _seenQuery.Vpn);
        Assert.AreEqual(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            FilterRender.Render(NetDiagFilters.Observations(_seenQuery))["SubmittedAt"]["$gte"].ToUniversalTime());
    }

    // Empty values are what a cleared form field sends: they are no filter and no error.
    [Test]
    public async Task ListMeasurements_EmptyValues_AreNoFilterAndNoError()
    {
        var (status, _, _) = await Get("/api/network-diagnostics/measurements?battleTag=&nodeId=&asn=&vpn=&from=&to=&targetKey=");

        Assert.AreEqual(200, status);
        Assert.AreEqual(new NetDiagQuery(), _seenQuery with { RunId = null, MeasurementId = null });
    }

    // P8: a value that does not parse answers 400 naming the query parameter; the service is not asked.
    [TestCase("nodeId=abc", "nodeId")]
    [TestCase("nodeId=99999999999", "nodeId")]
    [TestCase("asn=x", "asn")]
    [TestCase("vpn=maybe", "vpn")]
    [TestCase("from=yesterday", "from")]
    [TestCase("to=2026-13-45", "to")]
    [TestCase("page=x", "page")]
    [TestCase("pageSize=1.5", "pageSize")]
    [TestCase("NODEID=abc", "nodeId")]
    [TestCase("to=bad&asn=bad", "asn")]
    public async Task ListMeasurements_UnparsableValue_400NamingTheParameter(string query, string key)
    {
        var (status, body, _) = await Get($"/api/network-diagnostics/measurements?{query}");

        Assert.AreEqual((400, $$"""{"error":"invalid query parameter: {{key}}"}"""), (status, body));
        _queries.Verify(q => q.ListMeasurementsAsync(It.IsAny<NetDiagQuery>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task ListMeasurements_TruncatedLookup_SendsTheHeader()
    {
        _queries.Setup(q => q.ListMeasurementsAsync(It.IsAny<NetDiagQuery>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(NetDiagResult.Ok(new PageDto<MeasurementSummaryDto>([], 0)) with { Truncated = true });

        var (status, body, response) = await Get("/api/network-diagnostics/measurements?method=TcpPing");

        Assert.AreEqual((200, """{"items":[],"total":0}"""), (status, body));
        Assert.AreEqual("true", response.Headers.GetValues("X-NetDiag-Truncated").Single());
    }

    [TestCase("", false)]
    [TestCase("?raw=false", false)]
    [TestCase("?raw=true", true)]
    public async Task GetMeasurement_RoutesTheIdAndRaw(string query, bool raw)
    {
        _queries.Setup(q => q.GetMeasurementAsync("6f1d0c9e-4c1b-4a8e-9d55-0b6b8f0f7a11", raw)).ReturnsAsync(NetDiagResult.Ok(new { measurementId = "x" }));

        var (status, body, _) = await Get($"/api/network-diagnostics/measurements/6f1d0c9e-4c1b-4a8e-9d55-0b6b8f0f7a11{query}");

        Assert.AreEqual((200, """{"measurementId":"x"}"""), (status, body));
    }

    [Test]
    public async Task GetMeasurement_UnparsableRaw_400NamingTheParameter()
    {
        var (status, body, _) = await Get("/api/network-diagnostics/measurements/m1?raw=maybe");

        Assert.AreEqual((400, """{"error":"invalid query parameter: raw"}"""), (status, body));
        _queries.Verify(q => q.GetMeasurementAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Test]
    public async Task GetMeasurement_ServiceError_KeepsStatusAndErrorShape()
    {
        _queries.Setup(q => q.GetMeasurementAsync("nope", false)).ReturnsAsync(NetDiagResult.Error(404, "not_found"));

        var (status, body, _) = await Get("/api/network-diagnostics/measurements/nope");

        Assert.AreEqual((404, """{"error":"not_found"}"""), (status, body));
    }

    private static NetworkDiagnosticObservation Obs(string measurementId) => new()
    {
        Id = ObjectId.GenerateNewId(),
        MeasurementId = measurementId,
        RunId = "r1",
        BattleTag = "Name#1234",
        SubmittedAt = new DateTime(2026, 10, 12, 10, 2, 0, DateTimeKind.Utc),
        TargetKey = "flo:12",
        Path = "direct",
        Method = "TcpPing",
        Outcome = "ok",
        Reason = "=cmd",
        Sent = 10,
        Received = 10,
    };

    private const string ObservationsUrl = "/api/network-diagnostics/observations";

    // The website's request (NetworkDiagnosticsService.getObservations): shared filters, format=json, cursor, limit.
    [Test]
    public async Task GetObservations_Json_BindsFiltersCursorAndLimit_AndAnswersItemsAndNextCursor()
    {
        NetDiagQuery seen = null;
        _queries.Setup(q => q.GetObservationsAsync(It.IsAny<NetDiagQuery>(), "507f1f77bcf86cd799439011", 5000, It.IsAny<CancellationToken>()))
            .Callback<NetDiagQuery, string, int, CancellationToken>((query, _, _, _) => seen = query)
            .ReturnsAsync(NetDiagResult.Ok(new ObservationPage([ObservationDto.From(Obs("m1"))], "507f1f77bcf86cd799439012")));

        var (status, body, response) = await Get($"{ObservationsUrl}?battleTag=Name%231234&runId=r1&nodeId=12&format=json&cursor=507f1f77bcf86cd799439011&limit=5000");

        Assert.AreEqual(200, status);
        StringAssert.StartsWith("""{"items":[{"measurementId":"m1","runId":"r1","battleTag":"Name#1234","submittedAt":"2026-10-12T10:02:00Z",""", body);
        StringAssert.EndsWith("""],"nextCursor":"507f1f77bcf86cd799439012"}""", body);
        Assert.AreEqual("application/json", response.Content.Headers.ContentType!.MediaType);
        // The player filtered for, not the acting admin.
        Assert.AreEqual(("Name#1234", 12), (seen.BattleTag, seen.NodeId));
        Assert.AreEqual(new[] { "r1" }, seen.RunId);
        Assert.IsFalse(response.Headers.Contains("X-Next-Cursor"), "the JSON form carries the cursor in the body");
    }

    [TestCase("")]
    [TestCase("?format=")]
    public async Task GetObservations_Defaults_AreJson_NoCursor_Limit1000(string query)
    {
        _queries.Setup(q => q.GetObservationsAsync(It.IsAny<NetDiagQuery>(), null, 1000, It.IsAny<CancellationToken>()))
            .ReturnsAsync(NetDiagResult.Ok(new ObservationPage([], null)));

        var (status, body, _) = await Get(ObservationsUrl + query);

        Assert.AreEqual((200, """{"items":[],"nextCursor":null}"""), (status, body));
    }

    private void StreamAnswers(IAsyncCursor<NetworkDiagnosticObservation> rows, string nextCursor, string cursor = null, int limit = 1000) =>
        _queries.Setup(q => q.OpenObservationStreamAsync(It.IsAny<NetDiagQuery>(), cursor, limit, It.IsAny<CancellationToken>()))
            .ReturnsAsync(NetDiagResult.Ok(new ObservationStream(rows, nextCursor)));

    [Test]
    public async Task GetObservations_Csv_StreamsHeaderAndEveryBatch_WithTheCursorInTheHeader()
    {
        var rows = new FakeCursor<NetworkDiagnosticObservation>(() => [Obs("m1"), Obs("m2")], () => [Obs("m3")]);
        StreamAnswers(rows, "507f1f77bcf86cd799439012", cursor: "507f1f77bcf86cd799439011", limit: 3);

        var (status, body, response) = await Get($"{ObservationsUrl}?format=csv&limit=3&cursor=507f1f77bcf86cd799439011");

        var lines = body.Split("\r\n");
        Assert.AreEqual(200, status);
        Assert.AreEqual("text/csv; charset=utf-8", response.Content.Headers.ContentType!.ToString());
        Assert.AreEqual("507f1f77bcf86cd799439012", response.Headers.GetValues("X-Next-Cursor").Single());
        Assert.AreEqual(string.Join(",", ObservationFormats.CsvColumns), lines[0]);
        Assert.AreEqual(new[] { "m1", "m2", "m3" }, lines.Skip(1).Take(3).Select(l => l.Split(',')[0]));
        // Review Focus 6, on the wire.
        StringAssert.Contains(",ok,'=cmd,10,10,", lines[1]);
        Assert.AreEqual(5, lines.Length);
        Assert.AreEqual("", lines[4]);
        Assert.IsTrue(rows.Disposed, "the cursor is released with the request");
        _queries.Verify(q => q.GetObservationsAsync(It.IsAny<NetDiagQuery>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task GetObservations_Ndjson_StreamsOneObjectPerLine_AndTheLastPageHasNoCursorHeader()
    {
        StreamAnswers(new FakeCursor<NetworkDiagnosticObservation>(() => [Obs("m1")], () => [Obs("m2")]), null);

        var (status, body, response) = await Get($"{ObservationsUrl}?format=ndjson");

        var lines = body.Split('\n');
        Assert.AreEqual(200, status);
        Assert.AreEqual("application/x-ndjson; charset=utf-8", response.Content.Headers.ContentType!.ToString());
        Assert.IsFalse(response.Headers.Contains("X-Next-Cursor"));
        Assert.AreEqual(3, lines.Length);
        Assert.AreEqual("m2", System.Text.Json.JsonDocument.Parse(lines[1]).RootElement.GetProperty("measurementId").GetString());
        Assert.AreEqual("", lines[2]);
    }

    [Test]
    public async Task GetObservations_Ndjson_NoRows_IsAnEmptyBody()
    {
        StreamAnswers(new FakeCursor<NetworkDiagnosticObservation>(), null);

        var (status, body, _) = await Get($"{ObservationsUrl}?format=ndjson");

        Assert.AreEqual((200, ""), (status, body));
    }

    // The status line and the first rows are gone when a later batch fails. The connection is cut instead of ended:
    // the client gets a transport error, never a body that looks complete.
    [TestCase("csv")]
    [TestCase("ndjson")]
    public async Task GetObservations_Streamed_DatabaseErrorMidStream_CutsTheConnection(string format)
    {
        var rows = new FakeCursor<NetworkDiagnosticObservation>(() => [Obs("m1")], () => throw new MongoException("connection lost"));
        StreamAnswers(rows, null);

        using var response = await _http.GetAsync($"{ObservationsUrl}?format={format}", HttpCompletionOption.ResponseHeadersRead);

        Assert.AreEqual(200, (int)response.StatusCode, "the rows before the error were already on the wire");
        Assert.That(async () => await response.Content.ReadAsStringAsync(), Throws.InstanceOf<HttpRequestException>().Or.InstanceOf<IOException>());
        Assert.AreEqual(2, rows.BatchesRead);
        Assert.IsTrue(rows.Disposed);
    }

    // Before the first byte nothing is lost: the usual error answers apply to the streamed formats too.
    [Test]
    public async Task GetObservations_Streamed_ErrorBeforeTheFirstRow_IsAnOrdinaryErrorAnswer()
    {
        _queries.Setup(q => q.OpenObservationStreamAsync(It.IsAny<NetDiagQuery>(), "x", 1000, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminRequests.InvalidParameter("cursor"));
        _queries.Setup(q => q.OpenObservationStreamAsync(It.IsAny<NetDiagQuery>(), null, 1000, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MongoExecutionTimeoutException(Connection, "operation exceeded time limit"));

        var invalid = await Get($"{ObservationsUrl}?format=csv&cursor=x");
        var timeout = await Get($"{ObservationsUrl}?format=ndjson");

        Assert.AreEqual((400, """{"error":"invalid query parameter: cursor"}"""), (invalid.Status, invalid.Body));
        Assert.IsFalse(invalid.Response.Headers.Contains("X-Next-Cursor"));
        Assert.AreEqual((503, """{"error":"query_timeout"}"""), (timeout.Status, timeout.Body));
    }

    private static readonly MongoDB.Driver.Core.Connections.ConnectionId Connection =
        new(new MongoDB.Driver.Core.Servers.ServerId(new MongoDB.Driver.Core.Clusters.ClusterId(1), new System.Net.DnsEndPoint("localhost", 27017)));

    // P8.
    [TestCase("format=xml", "format")]
    [TestCase("format=CSV", "format")]
    [TestCase("format=json,csv", "format")]
    [TestCase("limit=abc", "limit")]
    [TestCase("limit=1.5", "limit")]
    [TestCase("limit=99999999999", "limit")]
    [TestCase("format=csv&asn=x", "asn")]
    [TestCase("format=xml&vpn=maybe", "vpn")]
    public async Task GetObservations_UnparsableOrUnknownValue_400NamingTheParameter(string query, string key)
    {
        var (status, body, _) = await Get($"{ObservationsUrl}?{query}");

        Assert.AreEqual((400, $$"""{"error":"invalid query parameter: {{key}}"}"""), (status, body));
        Assert.IsEmpty(_queries.Invocations);
    }

    [Test]
    public async Task GetObservations_ServiceError_KeepsStatusAndErrorShape()
    {
        _queries.Setup(q => q.GetObservationsAsync(It.IsAny<NetDiagQuery>(), null, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(NetDiagResult.Error(400, "limit must be in 1-5000"));

        var (status, body, _) = await Get($"{ObservationsUrl}?limit=0");

        Assert.AreEqual((400, """{"error":"limit must be in 1-5000"}"""), (status, body));
    }

    // The website's request (NetworkDiagnosticsService.getAggregate): groupBy joined with commas plus the shared filters.
    [Test]
    public async Task Aggregate_BindsGroupByAndFilters_AndAnswersGroupByAndRows()
    {
        NetDiagQuery seen = null;
        var row = new AggregateRowDto(new() { ["asn"] = 3320L, ["vpn"] = false, ["city"] = null }, 42, 22.1, 41, 0, 0.05, 0, "Deutsche Telekom AG", 52.1, 13);
        _queries.Setup(q => q.AggregateAsync(It.IsAny<NetDiagQuery>(), "asn,vpn,city", It.IsAny<CancellationToken>()))
            .Callback<NetDiagQuery, string, CancellationToken>((query, _, _) => seen = query)
            .ReturnsAsync(NetDiagResult.Ok(new AggregateResponseDto(["asn", "vpn", "city"], [row])));

        var (status, body, response) = await Get("/api/network-diagnostics/aggregate?runId=r1&battleTag=Name%231234&groupBy=asn%2Cvpn%2Ccity");

        Assert.AreEqual(200, status);
        Assert.AreEqual(
            """{"groupBy":["asn","vpn","city"],"rows":[{"key":{"asn":3320,"vpn":false,"city":null},"count":42,"p50MsMedian":22.1,"p50MsP90":41,"lossPctMedian":0,"shareLossOver1Pct":0.05,"shareUnreachable":0,"asnOrg":"Deutsche Telekom AG","lat":52.1,"lon":13}]}""",
            body);
        Assert.AreEqual("Name#1234", seen.BattleTag);
        Assert.AreEqual(new[] { "r1" }, seen.RunId);
        Assert.IsFalse(response.Headers.Contains("X-NetDiag-Truncated"));
    }

    [Test]
    public async Task Aggregate_Truncated_SendsTheHeader_AndErrorsKeepTheirShape()
    {
        _queries.Setup(q => q.AggregateAsync(It.IsAny<NetDiagQuery>(), "path", It.IsAny<CancellationToken>()))
            .ReturnsAsync(NetDiagResult.Ok(new AggregateResponseDto(["path"], [])) with { Truncated = true });
        _queries.Setup(q => q.AggregateAsync(It.IsAny<NetDiagQuery>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminRequests.InvalidParameter("groupBy"));
        _queries.Setup(q => q.AggregateAsync(It.IsAny<NetDiagQuery>(), "os", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MongoExecutionTimeoutException(Connection, "operation exceeded time limit"));

        var truncated = await Get("/api/network-diagnostics/aggregate?groupBy=path");
        var missing = await Get("/api/network-diagnostics/aggregate");
        var unparsable = await Get("/api/network-diagnostics/aggregate?groupBy=path&nodeId=abc");
        var timeout = await Get("/api/network-diagnostics/aggregate?groupBy=os");

        Assert.AreEqual((200, """{"groupBy":["path"],"rows":[]}"""), (truncated.Status, truncated.Body));
        Assert.AreEqual("true", truncated.Response.Headers.GetValues("X-NetDiag-Truncated").Single());
        Assert.AreEqual((400, """{"error":"invalid query parameter: groupBy"}"""), (missing.Status, missing.Body));
        Assert.AreEqual((400, """{"error":"invalid query parameter: nodeId"}"""), (unparsable.Status, unparsable.Body));
        Assert.AreEqual((503, """{"error":"query_timeout"}"""), (timeout.Status, timeout.Body));
    }

    // The request's cancellation token reaches the service: a caller that hangs up stops the query.
    [Test]
    public async Task ClientHangsUp_CancelsTheQuery()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _queries.Setup(q => q.AggregateAsync(It.IsAny<NetDiagQuery>(), "path", It.IsAny<CancellationToken>()))
            .Returns<NetDiagQuery, string, CancellationToken>(async (_, _, token) =>
            {
                await using var registration = token.Register(() => cancelled.TrySetResult());
                entered.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);
                return NetDiagResult.Ok(new AggregateResponseDto(["path"], []));
            });
        using var hangUp = new CancellationTokenSource();
        var request = _http.GetAsync("/api/network-diagnostics/aggregate?groupBy=path", hangUp.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        hangUp.Cancel();

        Assert.That(async () => await request, Throws.InstanceOf<OperationCanceledException>());
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private sealed class OnlyTheAdminController : ControllerFeatureProvider
    {
        protected override bool IsController(TypeInfo typeInfo) => typeInfo.AsType() == typeof(NetworkDiagnosticsAdminController);
    }

    /// <summary>Swaps BearerHasPermissionFilter for a filter with its one side effect on the action arguments.</summary>
    private sealed class ActingAdminStandIn : IActionModelConvention
    {
        public void Apply(ActionModel action)
        {
            var permissionFilters = action.Filters.OfType<BearerHasPermissionFilter>().ToList();
            Assert.AreEqual(1, permissionFilters.Count, action.ActionName);
            action.Filters.Remove(permissionFilters[0]);
            action.Filters.Add(new InjectActingAdmin());
        }
    }

    private sealed class InjectActingAdmin : IActionFilter
    {
        // Same statement as BearerHasPermissionFilter.OnActionExecutionAsync.
        public void OnActionExecuting(ActionExecutingContext context) => context.ActionArguments["battleTag"] = ActingAdmin;

        public void OnActionExecuted(ActionExecutedContext context) { }
    }
}
