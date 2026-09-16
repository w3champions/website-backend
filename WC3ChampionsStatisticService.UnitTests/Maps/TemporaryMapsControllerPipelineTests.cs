using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using W3C.Domain.MatchmakingService;
using W3C.Domain.UpdateService;
using W3ChampionsStatisticService.Maps;
using W3ChampionsStatisticService.Sessions;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using WC3ChampionsStatisticService.Tests.WebApi;

namespace WC3ChampionsStatisticService.Tests.Maps;

/// <summary>
/// Real round trips through Kestrel and the MVC pipeline, for the answers that depend on framework behaviour the
/// action-level tests bypass: Kestrel's own 413 for a body above the raised limit, the binder's null for a missing
/// proofHash query, the auth filter in front of both routes, and the bare answers — which MVC's [ApiController]
/// client-error mapping would wrap in a ProblemDetails body, and Appendix A.4 says carry nothing. The production
/// registrations (<see cref="MapServiceExtensions.AddMapServices"/>) and the real auth filter are used, over the same
/// host doubles as the DI smoke test; the player token is the stubbed auth service's.
/// </summary>
[TestFixture]
public class TemporaryMapsControllerPipelineTests : TemporaryMapUploadServiceTestBase
{
    private const string PlayerToken = "player-token";

    [Test]
    public async Task ABodyAboveTheRaisedLimit_IsKestrels413_AnsweredAsFileTooLarge()
    {
        // Content-Length one byte over the per-action ceiling, sent with Expect: 100-continue so the client waits for
        // Kestrel's verdict before sending a single body byte: Kestrel checks the declared length against the limit the
        // resource filter raised on the first body read and rejects the request there, inside the action.
        var handler = new ScriptedHttpHandler();
        await using var host = await StartHostAsync(handler);
        using var client = new HttpClient(new SocketsHttpHandler { Expect100ContinueTimeout = HangGuard }) { BaseAddress = host.BaseAddress };
        var (_, contentType) = BuildMultipartBytes(Metadata(), "abc"u8.ToArray());
        var request = new HttpRequestMessage(HttpMethod.Post, "api/maps/temporary")
        {
            Content = new DeclaredLengthContent(TemporaryMapLimits.TransportBodyBytes + 1, contentType),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", PlayerToken);
        request.Headers.ExpectContinue = true;

        var response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.RequestEntityTooLarge));
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());
        Assert.That(body.Properties().Select(p => p.Name), Is.EqualTo(new[] { "code" }));
        Assert.That(body["code"]!.Value<string>(), Is.EqualTo("FILE_TOO_LARGE"));
        Assert.That(handler.Requests, Is.Empty, "no byte was spooled, so no upstream was asked");
        Assert.That(host.Services.GetRequiredService<TemporaryMapUploadGate>().InFlight, Is.Zero, "the slot was released");
    }

    [Test]
    public async Task AStatusRequestWithoutAProofHash_Is404Unknown_WithoutAskingMatchmaking()
    {
        // No query at all: the binder hands the action a null proofHash (no [ApiController], so no implicit [Required]
        // and no automatic 400), and the action answers "unknown" locally.
        var handler = new ScriptedHttpHandler();
        await using var host = await StartHostAsync(handler);
        var request = new HttpRequestMessage(HttpMethod.Get, "api/maps/temporary/status");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", PlayerToken);

        var response = await host.Client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());
        Assert.That(body.Properties().Select(p => p.Name), Is.EqualTo(new[] { "state" }));
        Assert.That(body["state"]!.Value<string>(), Is.EqualTo("unknown"));
        Assert.That(handler.Requests, Is.Empty, "a proofHash that can never match is not a free upstream probe");
    }

    // ---- The bare answers: a status and nothing else ---------------------------------------

    [Test]
    public async Task AThrottledStatusRequest_Is429_WithNoBodyAtAll()
    {
        // A.4: "429. Nothing else is returned." MVC's client-error mapping would otherwise answer a ProblemDetails body.
        var handler = new ScriptedHttpHandler();
        await using var host = await StartHostAsync(handler);
        var limiter = host.Services.GetRequiredService<MintRateLimiter>();
        for (var i = 0; i < TemporaryMapLimits.PrecheckPerBattleTagPerMinute; i++)
        {
            limiter.TryAcquire("tm-precheck:" + BattleTag, TemporaryMapLimits.PrecheckPerBattleTagPerMinute, DateTime.UtcNow, TemporaryMapLimits.PrecheckQuotaWindow, out _);
        }

        var response = await host.Client.SendAsync(StatusRequest(ProofHash));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        await AssertNoBody(response);
        Assert.That(handler.Requests, Is.Empty, "a throttled pre-check never reaches matchmaking");
    }

    [Test]
    public async Task AStatusRequestMatchmakingCannotAnswer_Is502_WithNoBodyAtAll()
    {
        var handler = new ScriptedHttpHandler().On(r => r.RequestUri!.AbsolutePath.Contains("/by-proof-hash/", StringComparison.Ordinal), TransportFails());
        await using var host = await StartHostAsync(handler);

        var response = await host.Client.SendAsync(StatusRequest(ProofHash));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
        await AssertNoBody(response);
    }

    [Test]
    public async Task AnUploadThatCannotBeSpooled_Is500_WithNoBodyAtAll()
    {
        // The service resolved for the controller spools into a directory that cannot exist (a file sits at its parent);
        // the reader's TemporaryMapSpoolException is the bare 500 of the CROSS-REPO ruling, and it must stay bare.
        Directory.CreateDirectory(TestRoot);
        var blocker = Path.Combine(TestRoot, "not-a-directory");
        File.WriteAllText(blocker, "");
        var handler = new ScriptedHttpHandler();
        await using var host = await StartHostAsync(handler, spoolDirectory: Path.Combine(blocker, "spool"));
        var (bytes, contentType) = BuildMultipartBytes(Metadata(), "abc"u8.ToArray());
        var request = new HttpRequestMessage(HttpMethod.Post, "api/maps/temporary")
        {
            Content = new ByteArrayContent(bytes) { Headers = { ContentType = MediaTypeHeaderValue.Parse(contentType) } },
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", PlayerToken);

        var response = await host.Client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        await AssertNoBody(response);
        Assert.That(handler.Requests, Is.Empty, "nothing was stored, so no upstream was asked");
        Assert.That(host.Services.GetRequiredService<TemporaryMapUploadGate>().InFlight, Is.Zero);
    }

    // ---- The auth filter, in front of both routes ---------------------------------------------

    [TestCase("GET", "api/maps/temporary/status?proofHash=" + ProofHash)]
    [TestCase("POST", "api/maps/temporary")]
    public async Task ARequestWithoutAPlayerToken_IsTheFilters401(string method, string route)
    {
        // The filter's own body, not the mapping's and not the controller's: the controller is never reached.
        var handler = new ScriptedHttpHandler();
        await using var host = await StartHostAsync(handler);
        var request = new HttpRequestMessage(new HttpMethod(method), route);
        if (method == "POST")
        {
            var (bytes, contentType) = BuildMultipartBytes(Metadata(), "abc"u8.ToArray());
            request.Content = new ByteArrayContent(bytes) { Headers = { ContentType = MediaTypeHeaderValue.Parse(contentType) } };
        }

        var response = await host.Client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("{\"error\":\"Invalid token\"}"));
        Assert.That(handler.Requests, Is.Empty);
        Assert.That(host.Services.GetRequiredService<TemporaryMapUploadGate>().InFlight, Is.Zero, "no slot was ever taken");
    }

    // ---- Helpers ------------------------------------------------------------------------------

    private static HttpRequestMessage StatusRequest(string proofHash)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "api/maps/temporary/status?proofHash=" + proofHash);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", PlayerToken);
        return request;
    }

    /// <summary>A status and nothing else: no body byte, no content type.</summary>
    private static async Task AssertNoBody(HttpResponseMessage response)
    {
        Assert.That(response.Content.Headers.ContentType, Is.Null, "no body, so no content type");
        Assert.That(response.Content.Headers.ContentLength, Is.Zero);
        Assert.That(await response.Content.ReadAsStringAsync(), Is.Empty);
    }

    /// <summary>
    /// The production registrations over the host doubles, with an auth service that knows one player token. With
    /// <paramref name="spoolDirectory"/>, the upload service the controller resolves spools there instead of the
    /// machine-wide directory (the service's spool seam is an init property, so it is registered on top).
    /// </summary>
    private static async Task<LoopbackMvcHost> StartHostAsync(ScriptedHttpHandler handler, string spoolDirectory = null)
    {
        var authService = new Mock<IW3CAuthenticationService>(MockBehavior.Strict);
        authService.Setup(s => s.GetUserByToken(PlayerToken, false)).Returns(new W3CUserAuthenticationDto { BattleTag = BattleTag });
        var activitySource = new ActivitySource(nameof(TemporaryMapsControllerPipelineTests));
        return await LoopbackMvcHost.StartAsync(
            services =>
            {
                AddHostDoubles(services, handler, activitySource, authService.Object).AddMapServices();
                if (spoolDirectory != null)
                {
                    services.AddSingleton(provider => new TemporaryMapUploadService(
                        provider.GetRequiredService<MatchmakingServiceClient>(),
                        provider.GetRequiredService<UpdateServiceClient>(),
                        provider.GetRequiredService<MintRateLimiter>(),
                        provider.GetRequiredService<ILogger<TemporaryMapUploadService>>())
                    {
                        SpoolDirectory = spoolDirectory,
                    });
                }
            },
            typeof(TemporaryMapsController));
    }

    /// <summary>
    /// A request body that declares <paramref name="length"/> and must never be asked for: with Expect: 100-continue the
    /// client sends it only once the server has invited it, and a server enforcing its limit never does.
    /// </summary>
    private sealed class DeclaredLengthContent : HttpContent
    {
        private readonly long _length;

        public DeclaredLengthContent(long length, string contentType)
        {
            _length = length;
            Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _length;
            return true;
        }

        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext context)
            => throw new InvalidOperationException("The server invited the body: its declared length was not checked against the limit before the first read.");

        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext context, CancellationToken cancellationToken)
            => SerializeToStreamAsync(stream, context);
    }
}
