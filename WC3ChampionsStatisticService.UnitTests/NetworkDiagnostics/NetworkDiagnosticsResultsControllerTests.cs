using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.NetworkDiagnostics;
using W3ChampionsStatisticService.NetworkDiagnostics.Api;
using W3ChampionsStatisticService.NetworkDiagnostics.GeoIp;
using W3ChampionsStatisticService.NetworkDiagnostics.Persistence;
using W3ChampionsStatisticService.NetworkDiagnostics.Results;
using W3ChampionsStatisticService.NetworkDiagnostics.Runs;
using W3ChampionsStatisticService.NetworkDiagnostics.Submission;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace WC3ChampionsStatisticService.Tests.NetworkDiagnostics;

/// <summary>
/// Runs the real request-decompression middleware (as Program.cs mounts it) in front of the real action, with the
/// request the launcher sends (tauri-plugin-netdiag env.rs: gzip body, application/json, X-Measurement-Token).
/// </summary>
[TestFixture]
public class NetworkDiagnosticsResultsControllerTests
{
    private const string Id = SubmissionFixtures.MeasurementId;
    private const string Player = "Name#1234";
    private const string Token = "a-measurement-token";
    private const string ResultsPath = "/api/network-diagnostics/results/" + Id;

    private static readonly DateTime Now = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

    private Mock<INetworkDiagnosticsAssignmentRepository> _assignments;
    private Mock<INetworkDiagnosticsResultRepository> _results;
    private NetworkDiagnosticsSubmissionService _service;
    private NetworkDiagnosticAssignment _assignment;
    private BuiltResult _stored;

    // The real submission service over repository doubles: the action and the service decide together when the
    // body is read, so they are tested together.
    [SetUp]
    public void SetUp()
    {
        _assignment = new NetworkDiagnosticAssignment
        {
            Id = Id,
            RunId = "run-1",
            BattleTag = Player,
            Token = Token,
            Status = AssignmentStatus.Pending,
            DeliverBy = Now.AddDays(1),
        };
        _assignments = new Mock<INetworkDiagnosticsAssignmentRepository>();
        _assignments.Setup(a => a.GetAsync(Id)).ReturnsAsync(() => _assignment);
        _assignments.Setup(a => a.TrySubmitAsync(Id, Player, Now)).ReturnsAsync(() => _assignment with { Status = AssignmentStatus.Submitted, SubmittedAt = Now });
        _stored = null;
        _results = new Mock<INetworkDiagnosticsResultRepository>();
        _results.Setup(r => r.InsertResultAsync(It.IsAny<BuiltResult>())).Callback<BuiltResult>(b => _stored = b).Returns(Task.CompletedTask);
        var geo = new Mock<IGeoIpLookup>();
        geo.Setup(g => g.Lookup(It.IsAny<IEnumerable<string>>())).Returns(new Dictionary<string, ClientGeo>());
        _service = new NetworkDiagnosticsSubmissionService(_assignments.Object, new Mock<INetworkDiagnosticsRunRepository>().Object, _results.Object,
            geo.Object, new FixedTimeProvider(new DateTimeOffset(Now)), new ListLogger<NetworkDiagnosticsSubmissionService>());
    }

    private void VerifyNothingStored()
    {
        _assignments.Verify(a => a.TrySubmitAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
        Assert.IsNull(_stored);
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    private static byte[] Gzip(byte[] content)
    {
        var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) gzip.Write(content);
        return compressed.ToArray();
    }

    private static string Json(IActionResult result) =>
        JsonSerializer.Serialize(((ObjectResult)result).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static async Task Through(string path, Stream body, string contentEncoding, IPAddress peer, RequestDelegate terminal)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRequestDecompression();
        await using var provider = services.BuildServiceProvider();
        var app = new ApplicationBuilder(provider);
        app.UseNetworkDiagnosticsRequestDecompression();
        app.Run(terminal);

        var context = new DefaultHttpContext { RequestServices = provider };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = path;
        context.Request.ContentType = "application/json";
        if (contentEncoding != null) context.Request.Headers.ContentEncoding = contentEncoding;
        context.Request.Body = body;
        context.Connection.RemoteIpAddress = peer;
        // What InjectActingPlayerFromAuthCodeFilter leaves behind for an authenticated player.
        context.Items[InjectActingPlayerFromAuthCodeFilter.ActingPlayerUserKey] = new W3CUserAuthenticationDto { BattleTag = Player };
        await app.Build()(context);
    }

    private Task<ObjectResult> Submit(byte[] wire, string contentEncoding = "gzip", IPAddress peer = null, string token = Token) =>
        Submit(new MemoryStream(wire), contentEncoding, peer, token);

    private async Task<ObjectResult> Submit(Stream body, string contentEncoding = "gzip", IPAddress peer = null, string token = Token)
    {
        IActionResult result = null;
        await Through(ResultsPath, body, contentEncoding, peer, async context =>
        {
            var controller = new NetworkDiagnosticsResultsController(_service)
            {
                ControllerContext = new ControllerContext { HttpContext = context },
            };
            result = await controller.Submit(Id, token, CancellationToken.None);
        });
        return (ObjectResult)result;
    }

    [TestCase("gzip")]
    [TestCase("GZIP")]
    public async Task TheLaunchersGzipSnakeCaseBody_IsStored_201(string contentEncoding)
    {
        var result = await Submit(Gzip(Utf8(SubmissionFixtures.LauncherProducedJson)), contentEncoding);

        Assert.AreEqual(201, result.StatusCode);
        Assert.AreEqual($$"""{"measurementId":"{{Id}}"}""", Json(result));
        Assert.AreEqual(Id, _stored.Measurement.Id);
        Assert.AreEqual("1.6.20", _stored.Measurement.LauncherVersion, "snake_case names are read (not MVC's camelCase [FromBody])");
        Assert.AreEqual("203.0.113.7", _stored.Measurement.PublicIpv4);
        Assert.AreEqual(2, _stored.Measurement.Targets.Count);
        Assert.AreEqual(Player, _stored.Measurement.BattleTag);
    }

    // No Content-Encoding means identity: plain JSON is read as it is (curl, tests).
    [Test]
    public async Task PlainJsonWithoutContentEncoding_IsAccepted()
    {
        var result = await Submit(Utf8(SubmissionFixtures.FullJson), contentEncoding: null);

        Assert.AreEqual(201, result.StatusCode);
        Assert.AreEqual(Id, _stored.Measurement.Id);
    }

    // Review Focus 7 + ruling: without the right token nothing is read from the request, inflated or parsed.
    [TestCase("another-token")]
    [TestCase("")]
    [TestCase(null)]
    public async Task WrongToken_403_BeforeTheBodyIsRead(string token)
    {
        var body = new ReadCountingStream(Gzip(Utf8(SubmissionFixtures.FullJson)));

        var result = await Submit(body, token: token);

        Assert.AreEqual(403, result.StatusCode);
        Assert.AreEqual("""{"error":"forbidden"}""", Json(result));
        Assert.AreEqual(0, body.Reads, "no read call reached the request stream");
        VerifyNothingStored();
    }

    [TestCase("Other#1111")]
    [TestCase(null)]
    public async Task SomeoneElsesOrUnknownMeasurement_403_BeforeTheBodyIsRead(string owner)
    {
        _assignment = owner == null ? null : _assignment with { BattleTag = owner };
        var body = new ReadCountingStream(Gzip(Utf8(SubmissionFixtures.FullJson)));

        var result = await Submit(body);

        Assert.AreEqual(403, result.StatusCode);
        Assert.AreEqual(0, body.Reads);
        VerifyNothingStored();
    }

    [Test]
    public async Task WithTheRightToken_TheBodyIsRead()
    {
        var body = new ReadCountingStream(Gzip(Utf8(SubmissionFixtures.FullJson)));

        var result = await Submit(body);

        Assert.AreEqual(201, result.StatusCode);
        Assert.Greater(body.Reads, 0);
    }

    [Test]
    public async Task AlreadySubmitted_409()
    {
        _assignment = _assignment with { Status = AssignmentStatus.Submitted };

        var result = await Submit(Gzip(Utf8(SubmissionFixtures.FullJson)));

        Assert.AreEqual(409, result.StatusCode);
        Assert.AreEqual("""{"error":"already_submitted"}""", Json(result));
        VerifyNothingStored();
    }

    [Test]
    public async Task InsertFailure_500()
    {
        _results.Setup(r => r.InsertResultAsync(It.IsAny<BuiltResult>())).ThrowsAsync(new TimeoutException("mongo"));

        var result = await Submit(Gzip(Utf8(SubmissionFixtures.FullJson)));

        Assert.AreEqual(500, result.StatusCode);
        Assert.AreEqual("""{"error":"internal_error"}""", Json(result));
    }

    private static byte[] TruncatedGzip()
    {
        var wire = Gzip(Utf8(SubmissionFixtures.FullJson));
        return wire[..(wire.Length / 2)];
    }

    private static byte[] Bomb() => Gzip(new byte[SubmissionValidator.MaxBodyBytes + 1024 * 1024]);

    public static TestCaseData[] RejectedBodies() =>
    [
        new TestCaseData(Utf8(SubmissionFixtures.FullJson), "gzip", "Malformed JSON").SetName("Rejected_PlainJsonDeclaredAsGzip"),
        new TestCaseData(Utf8("not gzip, not json"), "gzip", "Malformed JSON").SetName("Rejected_GarbageDeclaredAsGzip"),
        new TestCaseData(TruncatedGzip(), "gzip", "Malformed JSON").SetName("Rejected_TruncatedGzip"),
        new TestCaseData(Gzip(Utf8(SubmissionFixtures.FullJson)), null, "Malformed JSON").SetName("Rejected_GzipWithoutContentEncoding"),
        new TestCaseData(Gzip(Utf8(SubmissionFixtures.FullJson)), "zstd", "Malformed JSON").SetName("Rejected_UnknownContentEncoding"),
        new TestCaseData(Gzip(Utf8(SubmissionFixtures.FullJson)), "gzip, gzip", "Malformed JSON").SetName("Rejected_StackedContentEncodings"),
        new TestCaseData(Gzip(Utf8("{\"measurement_id\": ")), "gzip", "Malformed JSON").SetName("Rejected_GzipOfBrokenJson"),
        new TestCaseData(Array.Empty<byte>(), "gzip", "Missing body").SetName("Rejected_EmptyBodyDeclaredAsGzip"),
        new TestCaseData(Array.Empty<byte>(), null, "Missing body").SetName("Rejected_EmptyBody"),
        new TestCaseData(Bomb(), "gzip", "payload_too_large").SetName("Rejected_GzipBombOverTheCap"),
    ];

    // Every body problem is a clean 400 {"error": …}; nothing throws (a 500 would make the launcher retry for hours).
    [TestCaseSource(nameof(RejectedBodies))]
    public async Task BadBody_400_AndNothingIsStored(byte[] wire, string contentEncoding, string expected)
    {
        var result = await Submit(wire, contentEncoding);

        Assert.AreEqual(400, result.StatusCode);
        Assert.AreEqual($$"""{"error":"{{expected}}"}""", Json(result));
        VerifyNothingStored();
    }

    // P5 + ruling: the peer address is the client IP only when the body reports none and only when it is a public
    // IPv4 address. A real IPv6 peer is never squeezed into one; a proxy or container hop is not the client.
    [TestCase("198.51.100.7", "198.51.100.7")]
    [TestCase("::ffff:198.51.100.7", "198.51.100.7")]
    [TestCase("2001:db8::c633:6407", null)]   // its last four bytes read as 198.51.100.7
    [TestCase("172.18.0.5", null)]
    [TestCase("127.0.0.1", null)]
    [TestCase(null, null)]
    public async Task ThePeersPublicIpv4_IsTheFallbackClientIp(string peer, string expected)
    {
        var withoutPublicIp = SubmissionFixtures.FullJson.Replace("\"public_ipv4\": \"81.2.69.160\"", "\"public_ipv4\": null");

        await Submit(Gzip(Utf8(withoutPublicIp)), peer: peer == null ? null : IPAddress.Parse(peer));

        Assert.IsNull(_stored.Measurement.PublicIpv4);
        Assert.AreEqual(expected, _stored.Measurement.ClientIp);
    }

    // A reported public_ipv4 is taken as it is, whatever the peer address.
    [Test]
    public async Task TheReportedPublicIpv4_WinsOverThePeer()
    {
        await Submit(Gzip(Utf8(SubmissionFixtures.FullJson)), peer: IPAddress.Parse("198.51.100.7"));

        Assert.AreEqual("81.2.69.160", _stored.Measurement.ClientIp);
    }

    // Decompression is mounted for the results path only: every other endpoint keeps reading its body as sent.
    [TestCase(ResultsPath, false)]
    [TestCase("/API/Network-Diagnostics/Results/" + Id, false)]
    [TestCase("/api/network-diagnostics/runs", true)]
    [TestCase("/api/lag-reports", true)]
    public async Task OnlyTheResultsPath_IsDecompressed(string path, bool stillCompressed)
    {
        var wire = Gzip(Utf8(SubmissionFixtures.FullJson));
        byte[] seen = null;

        await Through(path, new MemoryStream(wire), "gzip", null, async context =>
        {
            var buffer = new MemoryStream();
            await context.Request.Body.CopyToAsync(buffer);
            seen = buffer.ToArray();
        });

        Assert.AreEqual(stillCompressed, seen.SequenceEqual(wire));
        if (!stillCompressed) Assert.AreEqual(SubmissionFixtures.FullJson, Encoding.UTF8.GetString(seen));
    }

    // The contract the launcher is built against (C5, A10) and the secrecy rules, pinned on the action itself.
    [Test]
    public void TheAction_MatchesTheLaunchersRequest_AndKeepsTheTokenOutOfTraces()
    {
        var controller = typeof(NetworkDiagnosticsResultsController);
        var action = controller.GetMethod(nameof(NetworkDiagnosticsResultsController.Submit));
        var parameters = action.GetParameters();
        var token = parameters.Single(p => p.Name == "measurementToken");

        Assert.AreEqual("api/network-diagnostics", controller.GetCustomAttribute<RouteAttribute>().Template);
        Assert.AreEqual("results/{measurementId}", action.GetCustomAttribute<HttpPostAttribute>().Template);
        Assert.IsNull(controller.GetCustomAttribute<ApiControllerAttribute>(), "errors are {error}, never ProblemDetails");
        Assert.IsNotNull(action.GetCustomAttribute<InjectActingPlayerAuthCodeAttribute>(), "player JWT; 401 without one");
        Assert.IsNotNull(action.GetCustomAttribute<DisableFormValueModelBindingAttribute>(), "MVC never reads the body as a form");
        Assert.AreEqual("X-Measurement-Token", token.GetCustomAttribute<FromHeaderAttribute>().Name);
        Assert.IsNotNull(token.GetCustomAttribute<NoTraceAttribute>(), "the token never reaches a trace (A10)");
        Assert.IsNotNull(parameters.Single(p => p.Name == "measurementId").GetCustomAttribute<FromRouteAttribute>());
        // Every parameter names its source (route, header) or is the cancellation token: nothing binds from a query
        // string or a form body, and the acting player comes from the filter, not from a parameter.
        CollectionAssert.AreEquivalent(new[] { "measurementId", "measurementToken", "cancellationToken" }, parameters.Select(p => p.Name));
        Assert.AreEqual(
            NetworkDiagnosticsResultsController.MaxWireBytes,
            ((IRequestSizeLimitMetadata)action.GetCustomAttribute<RequestSizeLimitAttribute>()).MaxRequestBodySize,
            "the compressed body is bounded too");
        Assert.Greater(NetworkDiagnosticsResultsController.MaxWireBytes, SubmissionValidator.MaxBodyBytes);
    }

    private sealed class ReadCountingStream(byte[] content) : MemoryStream(content)
    {
        public int Reads { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Reads++;
            return base.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            Reads++;
            return base.Read(buffer);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Reads++;
            return base.ReadAsync(buffer, cancellationToken);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Reads++;
            return base.ReadAsync(buffer, offset, count, cancellationToken);
        }
    }
}
