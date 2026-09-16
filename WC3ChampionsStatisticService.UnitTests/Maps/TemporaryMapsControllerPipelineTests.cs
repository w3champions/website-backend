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
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using W3ChampionsStatisticService.Maps;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using WC3ChampionsStatisticService.Tests.WebApi;

namespace WC3ChampionsStatisticService.Tests.Maps;

/// <summary>
/// Two real round trips through Kestrel and the MVC pipeline, for the two answers that depend on framework behaviour the
/// action-level tests bypass: Kestrel's own 413 for a body above the raised limit, and the binder's null for a missing
/// proofHash query. The production registrations (<see cref="MapServiceExtensions.AddMapServices"/>) and the real auth
/// filter are used, over the same host doubles as the DI smoke test; the player token is the stubbed auth service's.
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
        // No query at all: the binder hands the action a null proofHash (nullable reference types are off, so
        // [ApiController] adds no implicit [Required] and no automatic 400), and the action answers "unknown" locally.
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

    /// <summary>The production registrations over the host doubles, with an auth service that knows one player token.</summary>
    private static async Task<LoopbackMvcHost> StartHostAsync(ScriptedHttpHandler handler)
    {
        var authService = new Mock<IW3CAuthenticationService>(MockBehavior.Strict);
        authService.Setup(s => s.GetUserByToken(PlayerToken, false)).Returns(new W3CUserAuthenticationDto { BattleTag = BattleTag });
        var activitySource = new ActivitySource(nameof(TemporaryMapsControllerPipelineTests));
        return await LoopbackMvcHost.StartAsync(
            services => AddHostDoubles(services, handler, activitySource, authService.Object).AddMapServices(),
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
