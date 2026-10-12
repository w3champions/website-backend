using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
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
/// The C5 endpoint over real HTTP: Kestrel on a loopback port, routing, the decompression middleware as Program.cs
/// mounts it, the player-JWT filter, MVC binding and JSON output, and the real submission service. Only the
/// authentication service, the repositories and GeoIP are doubles. The requests are the ones tauri-plugin-netdiag (env.rs) sends.
/// </summary>
[TestFixture]
public class NetworkDiagnosticsResultsEndpointTests
{
    private const string Id = SubmissionFixtures.MeasurementId;
    private const string Player = "Name#1234";
    private const string Jwt = "player.jwt.value";
    private const string Token = "a-measurement-token";
    private const string ForbiddenBody = """{"error":"forbidden"}""";
    private const string TooLargeBody = """{"error":"payload_too_large"}""";

    private static readonly DateTime Now = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(3);

    private WebApplication _app;
    private HttpClient _http;
    private readonly Mock<INetworkDiagnosticsAssignmentRepository> _assignments = new();
    private readonly Mock<INetworkDiagnosticsResultRepository> _results = new();
    private BuiltResult _stored;

    [OneTimeSetUp]
    public async Task StartServer()
    {
        var geo = new Mock<IGeoIpLookup>();
        geo.Setup(g => g.Lookup(It.IsAny<IEnumerable<string>>())).Returns(new Dictionary<string, ClientGeo>());
        var auth = new Mock<IW3CAuthenticationService>();
        auth.Setup(a => a.GetUserByToken(Jwt, false)).Returns(new W3CUserAuthenticationDto { BattleTag = Player });

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(NetworkDiagnosticsResultsController).Assembly)
            .ConfigureApplicationPartManager(parts =>
            {
                parts.FeatureProviders.Clear();
                parts.FeatureProviders.Add(new OnlyTheResultsController());
            });
        builder.Services.AddRequestDecompression();
        builder.Services.AddSingleton(auth.Object);
        builder.Services.AddTransient<InjectActingPlayerFromAuthCodeFilter>();
        builder.Services.AddSingleton<INetworkDiagnosticsSubmissionService>(new NetworkDiagnosticsSubmissionService(
            _assignments.Object, new Mock<INetworkDiagnosticsRunRepository>().Object, _results.Object, geo.Object,
            new FixedTimeProvider(new DateTimeOffset(Now)), new ListLogger<NetworkDiagnosticsSubmissionService>()));

        _app = builder.Build();
        _app.UseRouting();
        _app.UseNetworkDiagnosticsRequestDecompression();
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
        _stored = null;
        _assignments.Reset();
        // Every measurement id belongs to the player and is open; the id is kept exactly as it was asked for.
        _assignments.Setup(a => a.GetAsync(It.IsAny<string>())).ReturnsAsync((string id) => Assignment(id));
        _assignments.Setup(a => a.TrySubmitAsync(It.IsAny<string>(), Player, Now))
            .ReturnsAsync((string id, string _, DateTime _) => Assignment(id) with { Status = AssignmentStatus.Submitted, SubmittedAt = Now });
        _results.Reset();
        _results.Setup(r => r.InsertResultAsync(It.IsAny<BuiltResult>())).Callback<BuiltResult>(b => _stored = b).Returns(Task.CompletedTask);
    }

    private static NetworkDiagnosticAssignment Assignment(string id) => new()
    {
        Id = id,
        RunId = "run-1",
        BattleTag = Player,
        Token = Token,
        Status = AssignmentStatus.Pending,
        DeliverBy = Now.AddDays(1),
    };

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    private static byte[] Gzip(byte[] content)
    {
        var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) gzip.Write(content);
        return compressed.ToArray();
    }

    /// <summary>The launcher's request: POST, Bearer JWT, token header, application/json, Content-Encoding: gzip.</summary>
    private static HttpRequestMessage LauncherRequest(byte[] wire, string measurementId = Id, string contentEncoding = "gzip", string jwt = Jwt, string token = Token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/network-diagnostics/results/{measurementId}")
        {
            Content = new ByteArrayContent(wire),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        if (contentEncoding != null) request.Content.Headers.ContentEncoding.Add(contentEncoding);
        if (jwt != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        // HTTP header names are case-insensitive; the launcher's HTTP stack sends them lower-cased.
        if (token != null) request.Headers.TryAddWithoutValidation("x-measurement-token", token);
        return request;
    }

    private async Task<(HttpStatusCode Status, string Body, string ContentType)> Send(HttpRequestMessage request)
    {
        using var response = await _http.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(), response.Content.Headers.ContentType?.MediaType);
    }

    private void VerifyNothingStored()
    {
        _assignments.Verify(a => a.TrySubmitAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
        Assert.IsNull(_stored);
    }

    /// <summary>
    /// Sends only a request head announcing a body that never comes, and returns the answer up to and including
    /// <paramref name="until"/>. An endpoint that answers has not waited for the body; one that tries to read it
    /// first sends nothing, and the timeout fails the test instead of hanging the fixture. (The answer is not read
    /// to its end: the server then waits some seconds for the announced body before it closes the connection.)
    /// </summary>
    private async Task<string> SendHeadOnly(string extraHeaders, long contentLength, string until)
    {
        var address = new Uri(_app.Urls.Single());
        using var timeout = new CancellationTokenSource(AnswerTimeout);
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(address.Host, address.Port, timeout.Token);
        var head = $"POST /api/network-diagnostics/results/{Id} HTTP/1.1\r\nHost: {address.Authority}\r\n{extraHeaders}"
            + $"Content-Length: {contentLength}\r\nConnection: close\r\n\r\n";
        await tcp.GetStream().WriteAsync(Encoding.ASCII.GetBytes(head), timeout.Token);

        var answer = new StringBuilder();
        var buffer = new byte[4096];
        while (!answer.ToString().Contains(until, StringComparison.Ordinal))
        {
            var read = await tcp.GetStream().ReadAsync(buffer, timeout.Token);
            if (read == 0) break;
            answer.Append(Encoding.ASCII.GetString(buffer, 0, read));
        }
        return answer.ToString();
    }

    [Test]
    public async Task TheLaunchersRequest_201_WithTheMeasurementId()
    {
        var (status, body, contentType) = await Send(LauncherRequest(Gzip(Utf8(SubmissionFixtures.LauncherProducedJson))));

        Assert.AreEqual(HttpStatusCode.Created, status);
        Assert.AreEqual($$"""{"measurementId":"{{Id}}"}""", body);
        Assert.AreEqual("application/json", contentType);
        Assert.AreEqual(Id, _stored.Measurement.Id);
        Assert.AreEqual(Player, _stored.Measurement.BattleTag);
        Assert.AreEqual(2, _stored.Measurement.Targets.Count);
        Assert.AreEqual("203.0.113.7", _stored.Measurement.ClientIp);
    }

    // The route value is used untouched: no lower-casing, no GUID re-formatting. It must equal the body's id as it is.
    [TestCase("6F1D0C9E-4C1B-4A8E-9D55-0B6B8F0F7A11")]
    [TestCase("{6f1d0c9e-4c1b-4a8e-9d55-0b6b8f0f7a11}")]
    [TestCase("not-a-guid")]
    public async Task TheRouteMeasurementId_IsUsedVerbatim(string routeId)
    {
        var json = SubmissionFixtures.FullJson.Replace(Id, routeId);

        var (status, body, _) = await Send(LauncherRequest(Gzip(Utf8(json)), measurementId: Uri.EscapeDataString(routeId)));

        Assert.AreEqual(HttpStatusCode.Created, status);
        Assert.AreEqual($$"""{"measurementId":"{{routeId}}"}""", body);
        _assignments.Verify(a => a.GetAsync(routeId), Times.Once);
        _assignments.Verify(a => a.TrySubmitAsync(routeId, Player, Now), Times.Once);
    }

    [Test]
    public async Task ABodyForAnotherSpellingOfTheId_400()
    {
        var (status, body, _) = await Send(LauncherRequest(Gzip(Utf8(SubmissionFixtures.FullJson)), measurementId: Id.ToUpperInvariant()));

        Assert.AreEqual(HttpStatusCode.BadRequest, status);
        Assert.AreEqual("""{"error":"measurement_id does not match the URL"}""", body);
        VerifyNothingStored();
    }

    // The loopback peer of this test is not a public address, so it is not taken as the client IP.
    [Test]
    public async Task WithoutAReportedPublicIp_ANonPublicPeerIsNotTheClientIp()
    {
        var json = SubmissionFixtures.FullJson.Replace("\"public_ipv4\": \"81.2.69.160\"", "\"public_ipv4\": null");

        var (status, _, _) = await Send(LauncherRequest(Gzip(Utf8(json))));

        Assert.AreEqual(HttpStatusCode.Created, status);
        Assert.IsNull(_stored.Measurement.ClientIp);
    }

    // A9: the launcher retries a 401 (its auth plugin renews the JWT); the body is never looked at.
    [TestCase(null)]
    [TestCase("an.unknown.jwt")]
    public async Task WithoutAValidPlayerJwt_401_AndNothingIsLookedUp(string jwt)
    {
        var (status, _, _) = await Send(LauncherRequest(Gzip(Utf8(SubmissionFixtures.FullJson)), jwt: jwt));

        Assert.AreEqual(HttpStatusCode.Unauthorized, status);
        _assignments.Verify(a => a.GetAsync(It.IsAny<string>()), Times.Never);
        VerifyNothingStored();
    }

    [TestCase(null)]
    [TestCase("another-token")]
    public async Task MissingOrWrongTokenHeader_403(string token)
    {
        var (status, body, _) = await Send(LauncherRequest(Gzip(Utf8(SubmissionFixtures.FullJson)), token: token));

        Assert.AreEqual(HttpStatusCode.Forbidden, status);
        Assert.AreEqual("""{"error":"forbidden"}""", body);
        VerifyNothingStored();
    }

    // The 403 does not wait for the body: the request announces one and never sends it.
    [TestCase("X-Measurement-Token: another-token\r\n")]
    [TestCase("")]
    public async Task WrongToken_403_BeforeTheBodyIsRead(string tokenHeader)
    {
        var response = await SendHeadOnly($"Authorization: Bearer {Jwt}\r\n{tokenHeader}Content-Type: application/json\r\nContent-Encoding: gzip\r\n", contentLength: 1000, until: ForbiddenBody);

        StringAssert.StartsWith("HTTP/1.1 403 ", response);
        StringAssert.Contains(ForbiddenBody, response);
    }

    // No parameter of the action binds from a form: MVC must not read (and wait for) a form body before the
    // player is authenticated.
    [TestCase("application/x-www-form-urlencoded")]
    [TestCase("multipart/form-data; boundary=x")]
    public async Task AFormContentType_IsNotReadBeforeAuthentication_401(string contentType)
    {
        var response = await SendHeadOnly($"Content-Type: {contentType}\r\n", contentLength: 1000, until: "\r\n");

        StringAssert.StartsWith("HTTP/1.1 401 ", response);
    }

    [Test]
    public async Task ABodyThatIsNotGzip_400()
    {
        var (status, body, _) = await Send(LauncherRequest(Utf8(SubmissionFixtures.FullJson)));

        Assert.AreEqual(HttpStatusCode.BadRequest, status);
        Assert.AreEqual("""{"error":"Malformed JSON"}""", body);
        VerifyNothingStored();
    }

    // Review Focus 2: 9 MiB of zeros in a few KB of gzip.
    [Test]
    public async Task AGzipBomb_400_PayloadTooLarge()
    {
        var (status, body, _) = await Send(LauncherRequest(Gzip(new byte[SubmissionValidator.MaxBodyBytes + 1024 * 1024])));

        Assert.AreEqual(HttpStatusCode.BadRequest, status);
        Assert.AreEqual("""{"error":"payload_too_large"}""", body);
        VerifyNothingStored();
    }

    // The bytes on the wire are bounded too: a request announcing more than the limit is answered without its body
    // being taken in. (An HTTP client would still be uploading when the answer and the close arrive.)
    [TestCase("Content-Encoding: gzip\r\n")]
    [TestCase("")]
    public async Task MoreWireBytesThanTheLimit_400_PayloadTooLarge(string contentEncodingHeader)
    {
        var response = await SendHeadOnly(
            $"Authorization: Bearer {Jwt}\r\nX-Measurement-Token: {Token}\r\nContent-Type: application/json\r\n{contentEncodingHeader}",
            NetworkDiagnosticsResultsController.MaxWireBytes + 1,
            until: TooLargeBody);

        StringAssert.StartsWith("HTTP/1.1 400 ", response);
        StringAssert.Contains(TooLargeBody, response);
        VerifyNothingStored();
    }

    [Test]
    public async Task OnlyPostIsRouted()
    {
        using var response = await _http.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/api/network-diagnostics/results/{Id}"));

        Assert.AreEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    private sealed class OnlyTheResultsController : ControllerFeatureProvider
    {
        protected override bool IsController(TypeInfo typeInfo) => typeInfo.AsType() == typeof(NetworkDiagnosticsResultsController);
    }
}
