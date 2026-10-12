using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Moq;
using NUnit.Framework;
using W3C.Contracts.Admin.Permission;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.NetworkDiagnostics;
using W3ChampionsStatisticService.NetworkDiagnostics.Api;
using W3ChampionsStatisticService.NetworkDiagnostics.Results;
using W3ChampionsStatisticService.NetworkDiagnostics.Runs;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace WC3ChampionsStatisticService.Tests.NetworkDiagnostics;

[TestFixture]
public class NetworkDiagnosticsAdminControllerTests
{
    private const string ValidBody = """{"audience":{"kind":"SampleCount","count":5},"targets":{"floNodeIds":[12],"customTargets":[{"ip":"10.0.0.5"}]},"methods":["IcmpMtr"],"windowSeconds":90,"unknownProperty":1}""";

    private Mock<INetworkDiagnosticsRunService> _service;
    private Mock<INetworkDiagnosticsQueryService> _queries;
    private NetworkDiagnosticsAdminController _controller;

    [SetUp]
    public void SetUp()
    {
        // Strict: an action that answers 400 on its own must not have reached the service.
        _service = new Mock<INetworkDiagnosticsRunService>(MockBehavior.Strict);
        _queries = new Mock<INetworkDiagnosticsQueryService>(MockBehavior.Strict);
        _controller = new NetworkDiagnosticsAdminController(_service.Object, _queries.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    private void Body(Stream body, string contentType = "application/json")
    {
        _controller.Request.Body = body;
        _controller.Request.ContentType = contentType;
    }

    private void Body(string json, string contentType = "application/json") => Body(new MemoryStream(Encoding.UTF8.GetBytes(json)), contentType);

    private static string Json(IActionResult result) =>
        JsonSerializer.Serialize(((ObjectResult)result).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    [Test]
    public async Task CreateRun_ReadsTheCamelCaseBody_PassesTheActingAdminAndMapsTheStatus()
    {
        CreateRunRequest seen = null;
        _service.Setup(s => s.CreateRunAsync(It.IsAny<CreateRunRequest>(), "Admin#1"))
            .Callback<CreateRunRequest, string>((r, _) => seen = r).ReturnsAsync(new NetDiagResult(201, new { id = "r1" }));
        Body(ValidBody);

        var result = (ObjectResult)await _controller.CreateRun("Admin#1");

        Assert.AreEqual(201, result.StatusCode);
        Assert.AreEqual("""{"id":"r1"}""", Json(result));
        Assert.AreEqual(AudienceKind.SampleCount, seen.Audience.Kind);
        Assert.AreEqual(5, seen.Audience.Count);
        Assert.AreEqual(new[] { 12 }, seen.Targets.FloNodeIds);
        Assert.AreEqual("10.0.0.5", seen.Targets.CustomTargets.Single().Ip);
        Assert.AreEqual(new[] { "IcmpMtr" }, seen.Methods);
        Assert.AreEqual(90, seen.WindowSeconds);
    }

    // No [ApiController] and no [FromBody]: the content type never decides whether the body is read (no 415, no 500).
    [TestCase("text/plain")]
    [TestCase(null)]
    public async Task CreateRun_AnyContentType_IsReadAsJson(string contentType)
    {
        _service.Setup(s => s.CreateRunAsync(It.Is<CreateRunRequest>(r => r.WindowSeconds == 90), "Admin#1")).ReturnsAsync(NetDiagResult.Created(new { }));
        Body(ValidBody, contentType);

        Assert.AreEqual(201, ((ObjectResult)await _controller.CreateRun("Admin#1")).StatusCode);
    }

    [TestCase("{")]
    [TestCase("not json")]
    [TestCase("[]")]
    [TestCase("5")]
    [TestCase("""{"windowSeconds":"abc"}""")]
    [TestCase("""{"windowSeconds":99999999999}""")]
    [TestCase("""{"targets":{"floNodeIds":[null]}}""")]
    [TestCase("""{"targets":{"floNodeIds":"12"}}""")]
    [TestCase("""{"audience":[]}""")]
    [TestCase("""{"allowDuringGame":null}""")]
    public async Task CreateRun_MalformedJson_400InvalidJson_WithoutCallingTheService(string body)
    {
        Body(body);

        var result = (ObjectResult)await _controller.CreateRun("Admin#1");

        Assert.AreEqual(400, result.StatusCode);
        Assert.AreEqual("""{"error":"invalid_json"}""", Json(result));
    }

    [Test]
    public async Task CreateRun_InvalidUtf8_400InvalidJson()
    {
        // {"methods":["<0xFF>"]}
        Body(new MemoryStream([.. Encoding.ASCII.GetBytes("{\"methods\":[\""), 0xFF, .. Encoding.ASCII.GetBytes("\"]}")]));

        Assert.AreEqual("""{"error":"invalid_json"}""", Json(await _controller.CreateRun("Admin#1")));
    }

    // An absent body and a JSON null reach the service as null; its validator answers 400 "body is required".
    [TestCase("")]
    [TestCase("null")]
    public async Task CreateRun_EmptyOrNullBody_ReachesTheValidatorAsNull(string body)
    {
        _service.Setup(s => s.CreateRunAsync(null, "Admin#1")).ReturnsAsync(NetDiagResult.Error(400, "body is required"));
        Body(body);

        var result = (ObjectResult)await _controller.CreateRun("Admin#1");

        Assert.AreEqual(400, result.StatusCode);
        Assert.AreEqual("""{"error":"body is required"}""", Json(result));
    }

    [Test]
    public async Task CreateRun_BodyAtTheCap_IsAccepted()
    {
        _service.Setup(s => s.CreateRunAsync(It.IsAny<CreateRunRequest>(), "Admin#1")).ReturnsAsync(NetDiagResult.Created(new { }));
        var padding = new string(' ', AdminRequests.MaxBodyBytes - ValidBody.Length);
        Body(ValidBody + padding);

        Assert.AreEqual(201, ((ObjectResult)await _controller.CreateRun("Admin#1")).StatusCode);
    }

    [Test]
    public async Task CreateRun_BodyOverTheCap_400_AndTheRestIsNeverRead()
    {
        var body = new CountingStream(new MemoryStream(new byte[4 * AdminRequests.MaxBodyBytes]));
        Body(body);

        var result = (ObjectResult)await _controller.CreateRun("Admin#1");

        Assert.AreEqual(400, result.StatusCode);
        Assert.AreEqual("""{"error":"payload_too_large"}""", Json(result));
        Assert.That(body.BytesRead, Is.LessThanOrEqualTo(AdminRequests.MaxBodyBytes + 1));
    }

    [Test]
    public async Task CreateRun_DeclaredLengthOverTheCap_400_WithoutReadingAnything()
    {
        var body = new CountingStream(new MemoryStream(Encoding.UTF8.GetBytes(ValidBody)));
        Body(body);
        _controller.Request.ContentLength = AdminRequests.MaxBodyBytes + 1;

        Assert.AreEqual("""{"error":"payload_too_large"}""", Json(await _controller.CreateRun("Admin#1")));
        Assert.AreEqual(0, body.BytesRead);
    }

    // P8: a query value that does not bind (page=abc, from=yesterday) must not silently run with the default.
    [TestCase("pageSize")]
    [TestCase("page")]
    [TestCase("from")]
    [TestCase("to")]
    public async Task EveryAction_InvalidModelState_400NamingTheParameter(string key)
    {
        _controller.ModelState.AddModelError(key, "The value 'abc' is not valid.");
        Body(ValidBody);
        var expected = $$"""{"error":"invalid query parameter: {{key}}"}""";

        IActionResult[] results =
        [
            await _controller.GetTargets(),
            await _controller.CreateRun("Admin#1"),
            await _controller.ListRuns(null, null, null),
            await _controller.GetRun("r1"),
            await _controller.CancelRun("r1"),
            await _controller.ListMeasurements(new NetDiagQuery()),
            await _controller.GetMeasurement("m1"),
        ];

        foreach (var result in results)
        {
            Assert.AreEqual(400, ((ObjectResult)result).StatusCode);
            Assert.AreEqual(expected, Json(result));
        }
    }

    [Test]
    public void InvalidQuery_NamesTheFirstInvalidKey_ClippedAndIgnoresValidEntries()
    {
        // A bound value that carries no error is not a failure, whatever its validation state.
        var state = new ModelStateDictionary();
        state.SetModelValue("createdBy", "Admin#1", "Admin#1");
        Assert.IsNull(AdminRequests.InvalidQuery(state));

        state.AddModelError("to", "bad");
        state.AddModelError("from", "bad");
        state.AddModelError(new string('x', 500), "bad");

        Assert.AreEqual("""{"error":"invalid query parameter: from"}""",
            JsonSerializer.Serialize(AdminRequests.InvalidQuery(state).Body, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        var longOnly = new ModelStateDictionary();
        longOnly.AddModelError(new string('x', 500), "bad");
        var message = JsonSerializer.Serialize(AdminRequests.InvalidQuery(longOnly).Body);
        Assert.That(message.Length, Is.LessThan(120));
    }

    // A5: from/to reach the repository as UTC instants; page is passed through zero-based.
    [Test]
    public async Task ListRuns_ConvertsFromAndToToUtc_AndPassesPagingThrough()
    {
        var utc = new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc);
        var unspecified = new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Unspecified);
        var local = utc.ToLocalTime();
        DateTime? seenFrom = null, seenTo = null;
        _service.Setup(s => s.ListRunsAsync("Other#2", It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), 3, 50))
            .Callback<string, DateTime?, DateTime?, int, int>((_, from, to, _, _) => (seenFrom, seenTo) = (from, to))
            .ReturnsAsync(NetDiagResult.Ok(new PageDto<RunDto>([], 0)));

        var result = (ObjectResult)await _controller.ListRuns("Other#2", unspecified, local, 3, 50);

        Assert.AreEqual(200, result.StatusCode);
        Assert.AreEqual(utc, seenFrom);
        Assert.AreEqual(DateTimeKind.Utc, seenFrom!.Value.Kind, "a value without offset is taken as UTC, whatever the server's time zone");
        Assert.AreEqual(utc, seenTo);
        Assert.AreEqual(DateTimeKind.Utc, seenTo!.Value.Kind);
    }

    [Test]
    public async Task ListRuns_NoBounds_PassesNulls()
    {
        _service.Setup(s => s.ListRunsAsync(null, null, null, 0, 25)).ReturnsAsync(NetDiagResult.Ok(new PageDto<RunDto>([], 0)));

        Assert.AreEqual(200, ((ObjectResult)await _controller.ListRuns(null, null, null)).StatusCode);
    }

    [Test]
    public async Task ListMeasurements_PassesTheBoundFilterAndPaging_AndMapsTheBody()
    {
        var filter = new NetDiagQuery { BattleTag = "Name#1234", RunId = ["r1"] };
        _queries.Setup(q => q.ListMeasurementsAsync(filter, 3, 50)).ReturnsAsync(NetDiagResult.Ok(new PageDto<MeasurementSummaryDto>([], 7)));

        var result = (ObjectResult)await _controller.ListMeasurements(filter, 3, 50);

        Assert.AreEqual((200, """{"items":[],"total":7}"""), (result.StatusCode, Json(result)));
        Assert.IsFalse(_controller.Response.Headers.ContainsKey("X-NetDiag-Truncated"));
    }

    [Test]
    public async Task ListMeasurements_Defaults_AreTheFirstPageOf25()
    {
        _queries.Setup(q => q.ListMeasurementsAsync(It.IsAny<NetDiagQuery>(), 0, 25)).ReturnsAsync(NetDiagResult.Ok(new PageDto<MeasurementSummaryDto>([], 0)));

        Assert.AreEqual(200, ((ObjectResult)await _controller.ListMeasurements(new NetDiagQuery())).StatusCode);
    }

    // P6: a bounded lookup that hit its limit is announced in a header; the body keeps its C6 shape.
    [Test]
    public async Task ListMeasurements_TruncatedResult_SetsTheHeader_AndKeepsTheBody()
    {
        _queries.Setup(q => q.ListMeasurementsAsync(It.IsAny<NetDiagQuery>(), 0, 25))
            .ReturnsAsync(NetDiagResult.Ok(new PageDto<MeasurementSummaryDto>([], 0)) with { Truncated = true });

        var result = (ObjectResult)await _controller.ListMeasurements(new NetDiagQuery { TargetKey = "flo:12" });

        Assert.AreEqual((200, """{"items":[],"total":0}"""), (result.StatusCode, Json(result)));
        Assert.AreEqual("true", _controller.Response.Headers["X-NetDiag-Truncated"].ToString());
        Assert.AreEqual("X-NetDiag-Truncated", NetDiagConstants.TruncatedHeader);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task GetMeasurement_PassesIdAndRaw_AndMapsStatusAndBody(bool raw)
    {
        _queries.Setup(q => q.GetMeasurementAsync("m1", raw)).ReturnsAsync(NetDiagResult.Error(404, "not_found"));

        var result = (ObjectResult)await _controller.GetMeasurement("m1", raw);

        Assert.AreEqual((404, """{"error":"not_found"}"""), (result.StatusCode, Json(result)));
    }

    [Test]
    public async Task GetMeasurement_RawDefaultsToFalse()
    {
        _queries.Setup(q => q.GetMeasurementAsync("m1", false)).ReturnsAsync(NetDiagResult.Ok(new { }));

        Assert.AreEqual(200, ((ObjectResult)await _controller.GetMeasurement("m1")).StatusCode);
    }

    [Test]
    public async Task Targets_GetRun_Cancel_MapStatusAndBody()
    {
        _service.Setup(s => s.GetTargetsAsync()).ReturnsAsync(NetDiagResult.Error(502, "matchmaking_unavailable"));
        _service.Setup(s => s.GetRunAsync("r1")).ReturnsAsync(NetDiagResult.Error(404, "not_found"));
        _service.Setup(s => s.CancelRunAsync("r1")).ReturnsAsync(NetDiagResult.Error(409, "already_cancelled"));

        var targets = (ObjectResult)await _controller.GetTargets();
        var run = (ObjectResult)await _controller.GetRun("r1");
        var cancel = (ObjectResult)await _controller.CancelRun("r1");

        Assert.AreEqual((502, """{"error":"matchmaking_unavailable"}"""), (targets.StatusCode, Json(targets)));
        Assert.AreEqual((404, """{"error":"not_found"}"""), (run.StatusCode, Json(run)));
        Assert.AreEqual((409, """{"error":"already_cancelled"}"""), (cancel.StatusCode, Json(cancel)));
    }

    // Every action is admin-only with the Proxies permission.
    [Test]
    public void EveryAction_RequiresTheProxiesPermission_AndRoutesUnderTheC6Base()
    {
        var actions = typeof(NetworkDiagnosticsAdminController).GetMethods()
            .Where(m => m.DeclaringType == typeof(NetworkDiagnosticsAdminController) && m.IsPublic && !m.IsStatic)
            .ToList();

        Assert.AreEqual(7, actions.Count);
        foreach (var action in actions)
        {
            var filter = action.GetCustomAttributes(typeof(BearerHasPermissionFilter), false).Cast<BearerHasPermissionFilter>().SingleOrDefault();
            Assert.IsNotNull(filter, action.Name);
            Assert.AreEqual(EPermission.Proxies, filter.Permission, action.Name);
        }
        // The filter overwrites the "battleTag" argument with the acting admin: only CreateRun takes it, kept out of traces.
        var withBattleTag = actions.Where(a => a.GetParameters().Any(p => p.Name == "battleTag")).ToList();
        Assert.AreEqual(new[] { nameof(NetworkDiagnosticsAdminController.CreateRun) }, withBattleTag.Select(a => a.Name));
        Assert.IsNotEmpty(withBattleTag.Single().GetParameters().Single().GetCustomAttributes(typeof(NoTraceAttribute), false));
        var route = (RouteAttribute)typeof(NetworkDiagnosticsAdminController).GetCustomAttributes(typeof(RouteAttribute), false).Single();
        Assert.AreEqual("api/network-diagnostics", route.Template);
        Assert.IsEmpty(typeof(NetworkDiagnosticsAdminController).GetCustomAttributes(typeof(ApiControllerAttribute), true));
    }

    [Test]
    public void AsUtc_KeepsUtc_ConvertsLocal_AndTakesUnspecifiedAsUtc()
    {
        var utc = new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc);

        Assert.AreEqual(utc, NetDiagTime.AsUtc(utc));
        Assert.AreEqual(utc, NetDiagTime.AsUtc(utc.ToLocalTime()));
        Assert.AreEqual(DateTimeKind.Utc, NetDiagTime.AsUtc(utc.ToLocalTime()).Kind);
        Assert.AreEqual(utc, NetDiagTime.AsUtc(DateTime.SpecifyKind(utc, DateTimeKind.Unspecified)));
        Assert.AreEqual(DateTimeKind.Utc, NetDiagTime.AsUtc(DateTime.SpecifyKind(utc, DateTimeKind.Unspecified)).Kind);
    }

    private sealed class CountingStream(Stream inner) : Stream
    {
        public long BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            BytesRead += read;
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
