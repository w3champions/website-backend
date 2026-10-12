using System;
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

    private Mock<INetworkDiagnosticsSubmissionService> _service;
    private NetDiagSubmission _seenDto;
    private string _seenRemoteIp;

    [SetUp]
    public void SetUp()
    {
        // Strict: a request answered 400 by the controller must not have reached the service.
        _service = new Mock<INetworkDiagnosticsSubmissionService>(MockBehavior.Strict);
        _seenDto = null;
        _seenRemoteIp = "not called";
    }

    private void TheServiceAnswers(NetDiagResult answer) =>
        _service.Setup(s => s.SubmitAsync(Id, Player, Token, It.IsAny<NetDiagSubmission>(), It.IsAny<string>()))
            .Callback<string, string, string, NetDiagSubmission, string>((_, _, _, dto, remoteIp) => (_seenDto, _seenRemoteIp) = (dto, remoteIp))
            .ReturnsAsync(answer);

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    private static byte[] Gzip(byte[] content)
    {
        var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) gzip.Write(content);
        return compressed.ToArray();
    }

    private static string Json(IActionResult result) =>
        JsonSerializer.Serialize(((ObjectResult)result).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static async Task Through(string path, byte[] wire, string contentEncoding, IPAddress peer, RequestDelegate terminal)
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
        context.Request.Body = new MemoryStream(wire);
        context.Connection.RemoteIpAddress = peer;
        await app.Build()(context);
    }

    private async Task<ObjectResult> Submit(byte[] wire, string contentEncoding = "gzip", IPAddress peer = null)
    {
        IActionResult result = null;
        await Through(ResultsPath, wire, contentEncoding, peer, async context =>
        {
            var controller = new NetworkDiagnosticsResultsController(_service.Object)
            {
                ControllerContext = new ControllerContext { HttpContext = context },
            };
            result = await controller.Submit(Id, Player, Token, CancellationToken.None);
        });
        return (ObjectResult)result;
    }

    [TestCase("gzip")]
    [TestCase("GZIP")]
    public async Task TheLaunchersGzipSnakeCaseBody_ReachesTheService_AndItsAnswerIsReturned(string contentEncoding)
    {
        TheServiceAnswers(NetDiagResult.Created(new { measurementId = Id }));

        var result = await Submit(Gzip(Utf8(SubmissionFixtures.LauncherProducedJson)), contentEncoding);

        Assert.AreEqual(201, result.StatusCode);
        Assert.AreEqual($$"""{"measurementId":"{{Id}}"}""", Json(result));
        Assert.AreEqual(Id, _seenDto.MeasurementId, "snake_case names are read (not MVC's camelCase [FromBody])");
        Assert.AreEqual("1.6.20", _seenDto.LauncherVersion);
        Assert.AreEqual("203.0.113.7", _seenDto.PublicIpv4);
        Assert.AreEqual(2, _seenDto.Targets.Count);
    }

    // No Content-Encoding means identity: plain JSON is read as it is (curl, tests).
    [Test]
    public async Task PlainJsonWithoutContentEncoding_IsAccepted()
    {
        TheServiceAnswers(NetDiagResult.Created(new { measurementId = Id }));

        var result = await Submit(Utf8(SubmissionFixtures.FullJson), contentEncoding: null);

        Assert.AreEqual(201, result.StatusCode);
        Assert.AreEqual(Id, _seenDto.MeasurementId);
    }

    [TestCase(409, "already_submitted")]
    [TestCase(403, "forbidden")]
    [TestCase(500, "internal_error")]
    public async Task TheServicesErrorStatusAndBody_AreReturned(int status, string error)
    {
        TheServiceAnswers(NetDiagResult.Error(status, error));

        var result = await Submit(Gzip(Utf8(SubmissionFixtures.FullJson)));

        Assert.AreEqual(status, result.StatusCode);
        Assert.AreEqual($$"""{"error":"{{error}}"}""", Json(result));
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
    public async Task BadBody_400_WithoutCallingTheService(byte[] wire, string contentEncoding, string expected)
    {
        var result = await Submit(wire, contentEncoding);

        Assert.AreEqual(400, result.StatusCode);
        Assert.AreEqual($$"""{"error":"{{expected}}"}""", Json(result));
    }

    // P5: a real IPv6 peer has no IPv4 address; it is never squeezed into one.
    [TestCase("198.51.100.7", "198.51.100.7")]
    [TestCase("::ffff:198.51.100.7", "198.51.100.7")]
    [TestCase("2001:db8::1", null)]
    [TestCase(null, null)]
    public async Task ThePeersIpv4_IsPassedAsTheFallbackClientIp(string peer, string expected)
    {
        TheServiceAnswers(NetDiagResult.Created(new { measurementId = Id }));

        await Submit(Gzip(Utf8(SubmissionFixtures.FullJson)), peer: peer == null ? null : IPAddress.Parse(peer));

        Assert.AreEqual(expected, _seenRemoteIp);
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

        await Through(path, wire, "gzip", null, async context =>
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
        var token = action.GetParameters().Single(p => p.Name == "measurementToken");

        Assert.AreEqual("api/network-diagnostics", controller.GetCustomAttribute<RouteAttribute>().Template);
        Assert.AreEqual("results/{measurementId}", action.GetCustomAttribute<HttpPostAttribute>().Template);
        Assert.IsNull(controller.GetCustomAttribute<ApiControllerAttribute>(), "errors are {error}, never ProblemDetails");
        Assert.IsNotNull(action.GetCustomAttribute<InjectActingPlayerAuthCodeAttribute>(), "player JWT; 401 without one");
        Assert.IsNotNull(action.GetParameters().SingleOrDefault(p => p.Name == "actingPlayer" && p.ParameterType == typeof(string)));
        Assert.AreEqual("X-Measurement-Token", token.GetCustomAttribute<FromHeaderAttribute>().Name);
        Assert.IsNotNull(token.GetCustomAttribute<NoTraceAttribute>(), "the token never reaches a trace (A10)");
        Assert.IsFalse(action.GetParameters().Any(p => p.GetCustomAttribute<FromBodyAttribute>() != null), "the body is read by hand");
        Assert.AreEqual(
            NetworkDiagnosticsResultsController.MaxWireBytes,
            ((IRequestSizeLimitMetadata)action.GetCustomAttribute<RequestSizeLimitAttribute>()).MaxRequestBodySize,
            "the compressed body is bounded too");
        Assert.Greater(NetworkDiagnosticsResultsController.MaxWireBytes, SubmissionValidator.MaxBodyBytes);
    }
}
