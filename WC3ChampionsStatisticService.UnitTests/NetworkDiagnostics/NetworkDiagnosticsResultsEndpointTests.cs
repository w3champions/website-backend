using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
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
using W3ChampionsStatisticService.NetworkDiagnostics.Submission;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace WC3ChampionsStatisticService.Tests.NetworkDiagnostics;

/// <summary>
/// The C5 endpoint over real HTTP: Kestrel on a loopback port, routing, the decompression middleware as Program.cs
/// mounts it, the player-JWT filter, MVC header binding and JSON output. Only the authentication service and the
/// submission service are doubles. The requests are the ones tauri-plugin-netdiag (env.rs) sends.
/// </summary>
[TestFixture]
public class NetworkDiagnosticsResultsEndpointTests
{
    private const string Id = SubmissionFixtures.MeasurementId;
    private const string Player = "Name#1234";
    private const string Jwt = "player.jwt.value";
    private const string Token = "a-measurement-token";

    private WebApplication _app;
    private HttpClient _http;
    private Mock<INetworkDiagnosticsSubmissionService> _service;
    private NetDiagSubmission _seenDto;
    private string _seenRemoteIp;

    [OneTimeSetUp]
    public async Task StartServer()
    {
        _service = new Mock<INetworkDiagnosticsSubmissionService>();
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
        builder.Services.AddSingleton(_service.Object);

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
        _service.Reset();
        _seenDto = null;
        _seenRemoteIp = null;
        _service.Setup(s => s.SubmitAsync(It.IsAny<string>(), Player, Token, It.IsAny<NetDiagSubmission>(), It.IsAny<string>()))
            .Callback<string, string, string, NetDiagSubmission, string>((_, _, _, dto, remoteIp) => (_seenDto, _seenRemoteIp) = (dto, remoteIp))
            .ReturnsAsync((string id, string _, string _, NetDiagSubmission _, string _) => NetDiagResult.Created(new { measurementId = id }));
        // Anything else (no token, wrong token) is the service's 403.
        _service.Setup(s => s.SubmitAsync(It.IsAny<string>(), It.IsAny<string>(), It.Is<string>(t => t != Token), It.IsAny<NetDiagSubmission>(), It.IsAny<string>()))
            .ReturnsAsync(NetDiagResult.Error(403, "forbidden"));
    }

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

    private void VerifyServiceNeverCalled() =>
        _service.Verify(s => s.SubmitAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<NetDiagSubmission>(), It.IsAny<string>()), Times.Never);

    [Test]
    public async Task TheLaunchersRequest_201_WithTheMeasurementId()
    {
        var (status, body, contentType) = await Send(LauncherRequest(Gzip(Utf8(SubmissionFixtures.LauncherProducedJson))));

        Assert.AreEqual(HttpStatusCode.Created, status);
        Assert.AreEqual($$"""{"measurementId":"{{Id}}"}""", body);
        Assert.AreEqual("application/json", contentType);
        Assert.AreEqual(Id, _seenDto.MeasurementId);
        Assert.AreEqual(2, _seenDto.Targets.Count);
        Assert.AreEqual("127.0.0.1", _seenRemoteIp);
    }

    // The route value reaches the service untouched: no lower-casing, no GUID re-formatting.
    [TestCase("6F1D0C9E-4C1B-4A8E-9D55-0B6B8F0F7A11")]
    [TestCase("{6f1d0c9e-4c1b-4a8e-9d55-0b6b8f0f7a11}")]
    [TestCase("not-a-guid")]
    public async Task TheRouteMeasurementId_IsPassedVerbatim(string routeId)
    {
        var (status, body, _) = await Send(LauncherRequest(Gzip(Utf8(SubmissionFixtures.FullJson)), measurementId: Uri.EscapeDataString(routeId)));

        Assert.AreEqual(HttpStatusCode.Created, status);
        Assert.AreEqual($$"""{"measurementId":"{{routeId}}"}""", body);
        _service.Verify(s => s.SubmitAsync(routeId, Player, Token, It.IsAny<NetDiagSubmission>(), It.IsAny<string>()), Times.Once);
    }

    // A9: the launcher retries a 401 (its auth plugin renews the JWT); the body is never looked at.
    [TestCase(null)]
    [TestCase("an.unknown.jwt")]
    public async Task WithoutAValidPlayerJwt_401_AndTheServiceIsNotCalled(string jwt)
    {
        var (status, _, _) = await Send(LauncherRequest(Gzip(Utf8(SubmissionFixtures.FullJson)), jwt: jwt));

        Assert.AreEqual(HttpStatusCode.Unauthorized, status);
        VerifyServiceNeverCalled();
    }

    [TestCase(null)]
    [TestCase("another-token")]
    public async Task MissingOrWrongTokenHeader_IsTheServices403(string token)
    {
        var (status, body, _) = await Send(LauncherRequest(Gzip(Utf8(SubmissionFixtures.FullJson)), token: token));

        Assert.AreEqual(HttpStatusCode.Forbidden, status);
        Assert.AreEqual("""{"error":"forbidden"}""", body);
        _service.Verify(s => s.SubmitAsync(Id, Player, token, It.IsAny<NetDiagSubmission>(), It.IsAny<string>()), Times.Once);
    }

    [Test]
    public async Task ABodyThatIsNotGzip_400()
    {
        var (status, body, _) = await Send(LauncherRequest(Utf8(SubmissionFixtures.FullJson)));

        Assert.AreEqual(HttpStatusCode.BadRequest, status);
        Assert.AreEqual("""{"error":"Malformed JSON"}""", body);
        VerifyServiceNeverCalled();
    }

    // Review Focus 2: 9 MiB of zeros in a few KB of gzip.
    [Test]
    public async Task AGzipBomb_400_PayloadTooLarge()
    {
        var (status, body, _) = await Send(LauncherRequest(Gzip(new byte[SubmissionValidator.MaxBodyBytes + 1024 * 1024])));

        Assert.AreEqual(HttpStatusCode.BadRequest, status);
        Assert.AreEqual("""{"error":"payload_too_large"}""", body);
        VerifyServiceNeverCalled();
    }

    // The bytes on the wire are bounded too: a request announcing more than the limit is answered without its body
    // being taken in. Sent over a raw socket, headers only: an HTTP client would still be uploading when the answer
    // (and the close) arrives.
    [TestCase("Content-Encoding: gzip\r\n")]
    [TestCase("")]
    public async Task MoreWireBytesThanTheLimit_400_PayloadTooLarge(string contentEncodingHeader)
    {
        var address = new Uri(_app.Urls.Single());
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(address.Host, address.Port);
        var head = $"POST /api/network-diagnostics/results/{Id} HTTP/1.1\r\nHost: {address.Authority}\r\nAuthorization: Bearer {Jwt}\r\n"
            + $"X-Measurement-Token: {Token}\r\nContent-Type: application/json\r\n{contentEncodingHeader}"
            + $"Content-Length: {NetworkDiagnosticsResultsController.MaxWireBytes + 1}\r\nConnection: close\r\n\r\n";
        await tcp.GetStream().WriteAsync(Encoding.ASCII.GetBytes(head));

        using var reader = new StreamReader(tcp.GetStream(), Encoding.ASCII);
        var response = await reader.ReadToEndAsync();

        StringAssert.StartsWith("HTTP/1.1 400 ", response);
        StringAssert.Contains("""{"error":"payload_too_large"}""", response);
        VerifyServiceNeverCalled();
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
