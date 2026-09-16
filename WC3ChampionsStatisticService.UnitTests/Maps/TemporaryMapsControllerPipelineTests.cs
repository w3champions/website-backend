using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using W3C.Domain.MatchmakingService;
using W3C.Domain.UpdateService;
using W3ChampionsStatisticService.Maps;
using W3ChampionsStatisticService.Services.BackgroundTasks;
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
/// host doubles as the DI smoke test; the player token is the stubbed auth service's. The admin map-file passthrough
/// (<see cref="MapsController.CreateMapFile"/>) is hosted the same way for what its pipeline does before the permission
/// filter answers.
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
    public async Task ABodyAboveTheServersOwnCeiling_IsReadUnderTheRaisedLimit_AndIsNot413()
    {
        // [TemporaryMapUploadBodyLimit] raises the per-request ceiling to TransportBodyBytes; the server's own limit
        // (Kestrel's 30,000,000 default in this host, 128 MiB in Program.cs) must not be the one deciding. A real,
        // well-formed multipart body one MiB above the server's ceiling is streamed from a repeated zero buffer (nothing
        // large in memory) with Expect: 100-continue, so a 413 — which Kestrel decides against the declared
        // Content-Length on the reader's first read — would come back cleanly before any byte is sent. Under the
        // raised limit the whole file reaches the reader and the service's sha1 check answers 400 SHA1_MISMATCH.
        var handler = new ScriptedHttpHandler();
        await using var host = await StartHostAsync(handler);
        var serverCeiling = host.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value.Limits.MaxRequestBodySize!.Value;
        var fileBytes = serverCeiling + 1024 * 1024;
        Assert.That(fileBytes, Is.LessThan(TemporaryMapLimits.MaxFileBytes), "the file must be within the feature's own cap");
        var content = new MultipartFormDataContent(Boundary);
        content.Add(new StringContent(Metadata(), Encoding.UTF8, "application/json"), "metadata");
        content.Add(new StreamContent(new ZeroStream(fileBytes)), "mapFile", "upload.bin");
        var request = new HttpRequestMessage(HttpMethod.Post, "api/maps/temporary") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", PlayerToken);
        request.Headers.ExpectContinue = true;

        var response = await host.Client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "the ceiling was raised, so the body was read in full");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());
        Assert.That(body.Properties().Select(p => p.Name), Is.EqualTo(new[] { "code" }));
        Assert.That(body["code"]!.Value<string>(), Is.EqualTo("SHA1_MISMATCH"));
        Assert.That(handler.Requests, Is.Empty, "the sha1 check comes before any upstream call");
        Assert.That(host.Services.GetRequiredService<TemporaryMapUploadGate>().InFlight, Is.Zero, "the slot was released");
    }

    [Test]
    public async Task TheUploadAction_RunsUnderTheRaisedBodyLimitAndTheMinimumDataRate_AndTheStatusRouteDoesNot()
    {
        // What Kestrel's per-request features hold once the pipeline has run, recorded by a middleware in front of MVC:
        // only the upload action carries [TemporaryMapUploadBodyLimit], so only there is the size ceiling raised and the
        // data-rate floor set; the status route keeps the server's defaults (Kestrel's 240 B/s after 5 s).
        var observed = new Dictionary<string, (long? MaxBodySize, MinDataRate DataRate)>();
        var handler = new ScriptedHttpHandler().On(IsBySha1, Respond(HttpStatusCode.OK, Record(5811)));
        await using var host = await StartHostAsync(handler, configureApp: app => app.Use(async (context, next) =>
        {
            await next(context);
            observed[context.Request.Path] = (
                context.Features.Get<IHttpMaxRequestBodySizeFeature>()?.MaxRequestBodySize,
                context.Features.Get<IHttpMinRequestBodyDataRateFeature>()?.MinDataRate);
        }));
        var (bytes, contentType) = BuildMultipartBytes(Metadata(), "abc"u8.ToArray());
        var upload = new HttpRequestMessage(HttpMethod.Post, "api/maps/temporary")
        {
            Content = new ByteArrayContent(bytes) { Headers = { ContentType = MediaTypeHeaderValue.Parse(contentType) } },
        };
        upload.Headers.Authorization = new AuthenticationHeaderValue("Bearer", PlayerToken);

        Assert.That((await host.Client.SendAsync(upload)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await host.Client.SendAsync(StatusRequest(ProofHash))).StatusCode, Is.EqualTo(HttpStatusCode.BadGateway), "unscripted, and irrelevant here");

        var uploaded = observed["/api/maps/temporary"];
        Assert.That(uploaded.MaxBodySize, Is.EqualTo(TemporaryMapLimits.TransportBodyBytes));
        Assert.That(uploaded.DataRate, Is.Not.Null);
        Assert.That(uploaded.DataRate!.BytesPerSecond, Is.EqualTo(TemporaryMapLimits.MinUploadBytesPerSecond));
        Assert.That(uploaded.DataRate.GracePeriod, Is.EqualTo(TemporaryMapLimits.MinUploadGracePeriod));
        var status = observed["/api/maps/temporary/status"];
        var kestrelDefaults = new KestrelServerOptions().Limits;
        Assert.That(status.MaxBodySize, Is.EqualTo(kestrelDefaults.MaxRequestBodySize), "the server's own ceiling, untouched");
        Assert.That(status.DataRate?.BytesPerSecond, Is.EqualTo(kestrelDefaults.MinRequestBodyDataRate!.BytesPerSecond), "the server's own floor, untouched");
    }

    [Test]
    public async Task AClientThatStopsSendingAndGoesAway_ReleasesItsSlot_AndStoredNothing()
    {
        // The slot is taken before the first body byte and held while the body streams in. A client that drops the
        // connection mid-body must hand the slot back: nothing else could, and a slot held for good is a slot denied
        // to everyone. (A body below the data-rate floor takes another path — Kestrel flags the request and cancels
        // the pending read, the read fails with its 408 BadHttpRequestException, and the action aborts the connection
        // itself; TemporaryMapsControllerUploadTests pins that arm.)
        var handler = new ScriptedHttpHandler();
        await using var host = await StartHostAsync(handler, spoolDirectory: SpoolDirectory);
        var gate = host.Services.GetRequiredService<TemporaryMapUploadGate>();
        using var abandon = new CancellationTokenSource();
        var (_, contentType) = BuildMultipartBytes(Metadata(), "abc"u8.ToArray());
        var request = new HttpRequestMessage(HttpMethod.Post, "api/maps/temporary")
        {
            Content = new StallingMultipartContent(Boundary, Metadata(), contentType, abandon.Token),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", PlayerToken);

        var send = host.Client.SendAsync(request, abandon.Token);
        await WaitUntilAsync(() => gate.InFlight == 1, "the upload never took its slot");
        abandon.Cancel();

        Assert.CatchAsync<OperationCanceledException>(() => send);
        await WaitUntilAsync(() => gate.InFlight == 0, "the slot was never released after the client went away");
        Assert.That(handler.Requests, Is.Empty, "no byte reached the end of the body, so no upstream was asked");
        Assert.That(FilesIn(SpoolDirectory), Is.Empty, "the partial spool file was removed");
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
        // the reader's TemporaryMapSpoolException is a bare 500 by the cross-repo contract, and it must stay bare.
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

    // ---- ApiExplorer, which the Swagger document in Program.cs is built from --------------------

    [Test]
    public async Task BothRoutes_AreDescribedToApiExplorer()
    {
        // Without [ApiController] a controller is invisible to ApiExplorer (and so to the Swagger document Program.cs
        // publishes) unless it opts in; every other controller of the service is documented, and so must this one be.
        var handler = new ScriptedHttpHandler();
        await using var host = await StartHostAsync(handler);

        var described = host.Services.GetRequiredService<IApiDescriptionGroupCollectionProvider>()
            .ApiDescriptionGroups.Items.SelectMany(group => group.Items)
            .Select(description => description.HttpMethod + " " + description.RelativePath);

        Assert.That(described, Is.EquivalentTo(new[] { "GET api/maps/temporary/status", "POST api/maps/temporary" }));
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

    // ---- The admin map-file passthrough, sent without a token ----------------------------------

    [Test]
    public async Task TheAdminMapFilePassthrough_LeavesItsBodyToTheAction_SoModelBindingReadsNoByte()
    {
        // CreateMapFile forwards the multipart body to update-service as a stream. Its battleTag parameter (filled in
        // by the permission filter) makes MVC bind arguments, and the form value provider reads any multipart body
        // during binding — all of it, buffered, before the permission filter has run, leaving the action an empty
        // stream to forward. [DisableFormValueModelBinding] keeps binding off the body, as on the temporary upload.
        var handler = new ScriptedHttpHandler();

        var (observed, bodyLength) = await ObserveAdminMapFileRoutesAsync(handler);

        Assert.That(bodyLength, Is.GreaterThan(0));
        Assert.That(observed["POST /api/maps/7/files"].BytesRead, Is.Zero, "model binding must not consume the body the action forwards");
    }

    /// <summary>
    /// A multipart POST to the passthrough and a GET to its sibling route, both without a token, and what the pipeline
    /// did with each: the request features once the pipeline has run and the body bytes the application read, recorded
    /// by a middleware in front of MVC. The permission filter is an action filter, so the resource filters and model
    /// binding have run by the time it answers 401 — and it needs a real admin token this host cannot mint, so the 401
    /// is where these round trips end. Returns the observations by "METHOD /path" and the POST body's length.
    /// </summary>
    private static async Task<(Dictionary<string, RequestObservation> Observed, int BodyLength)> ObserveAdminMapFileRoutesAsync(ScriptedHttpHandler handler)
    {
        var observed = new Dictionary<string, RequestObservation>();
        await using var host = await StartHostAsync(handler, configureApp: app => app.Use(async (context, next) =>
        {
            var body = new CountingStream(context.Request.Body);
            context.Request.Body = body;
            await next(context);
            observed[context.Request.Method + " " + context.Request.Path] = new RequestObservation(
                context.Features.Get<IHttpMaxRequestBodySizeFeature>()?.MaxRequestBodySize,
                context.Features.Get<IHttpMinRequestBodyDataRateFeature>()?.MinDataRate,
                body.BytesRead);
        }), controllers: [typeof(MapsController)]);
        var (bytes, contentType) = BuildMultipartBytes(Metadata(), "abc"u8.ToArray());
        var upload = new HttpRequestMessage(HttpMethod.Post, "api/maps/7/files")
        {
            Content = new ByteArrayContent(bytes) { Headers = { ContentType = MediaTypeHeaderValue.Parse(contentType) } },
        };

        var uploaded = await host.Client.SendAsync(upload);
        var listed = await host.Client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "api/maps/7/files"));

        Assert.That(uploaded.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(await uploaded.Content.ReadAsStringAsync(), Is.EqualTo("{\"error\":\"Unauthorized\"}"), "the permission filter's own answer: the action was not reached");
        Assert.That(listed.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(handler.Requests, Is.Empty, "nothing reaches an upstream without an admin token");
        return (observed, bytes.Length);
    }

    private sealed record RequestObservation(long? MaxBodySize, MinDataRate DataRate, long BytesRead);

    /// <summary>A read-only pass-through over <paramref name="inner"/> that counts the bytes read through it.</summary>
    private sealed class CountingStream(Stream inner) : Stream
    {
        public long BytesRead { get; private set; }

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

        public override int Read(Span<byte> buffer) => Count(inner.Read(buffer));

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => Count(await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => Count(await inner.ReadAsync(buffer, cancellationToken));

        private int Count(int read)
        {
            BytesRead += read;
            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // ---- Helpers ------------------------------------------------------------------------------

    private static HttpRequestMessage StatusRequest(string proofHash)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "api/maps/temporary/status?proofHash=" + proofHash);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", PlayerToken);
        return request;
    }

    /// <summary>Polls <paramref name="condition"/> until it holds; fails the test after <see cref="HangGuard"/>.</summary>
    private static async Task WaitUntilAsync(Func<bool> condition, string otherwise)
    {
        var deadline = DateTime.UtcNow + HangGuard;
        while (!condition())
        {
            Assert.That(DateTime.UtcNow, Is.LessThan(deadline), otherwise);
            await Task.Delay(10);
        }
    }

    /// <summary>
    /// A multipart body whose metadata part and mapFile headers arrive at once, followed by the first bytes of the file,
    /// and then nothing: it waits on <paramref name="abandoned"/>, the token the test cancels to drop the connection.
    /// </summary>
    private sealed class StallingMultipartContent : HttpContent
    {
        private readonly byte[] _prefix;
        private readonly CancellationToken _abandoned;

        public StallingMultipartContent(string boundary, string metadata, string contentType, CancellationToken abandoned)
        {
            _abandoned = abandoned;
            _prefix = Encoding.UTF8.GetBytes(
                $"--{boundary}\r\nContent-Disposition: form-data; name=\"metadata\"\r\nContent-Type: application/json\r\n\r\n{metadata}\r\n" +
                $"--{boundary}\r\nContent-Disposition: form-data; name=\"mapFile\"; filename=\"upload.w3x\"\r\nContent-Type: application/octet-stream\r\n\r\nab");
            Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext context)
        {
            await stream.WriteAsync(_prefix, _abandoned);
            await stream.FlushAsync(_abandoned);
            await Task.Delay(Timeout.InfiniteTimeSpan, _abandoned);
        }

        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext context, CancellationToken cancellationToken)
            => SerializeToStreamAsync(stream, context);
    }

    /// <summary>A status and nothing else: no body byte, no content type.</summary>
    private static async Task AssertNoBody(HttpResponseMessage response)
    {
        Assert.That(response.Content.Headers.ContentType, Is.Null, "no body, so no content type");
        Assert.That(response.Content.Headers.ContentLength, Is.Zero);
        Assert.That(await response.Content.ReadAsStringAsync(), Is.Empty);
    }

    /// <summary>
    /// A read-only stream of <paramref name="length"/> zero bytes; nothing is held in memory. Like a file, it can be
    /// positioned past its end (reads then return 0) but never before its start.
    /// </summary>
    private sealed class ZeroStream(long length) : Stream
    {
        private long _position;

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => _position; set => Seek(value, SeekOrigin.Begin); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = (int)Math.Clamp(length - _position, 0, count);
            Array.Clear(buffer, offset, read);
            _position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            var target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                _ => length + offset,
            };
            return _position = target >= 0 ? target : throw new IOException("An attempt was made to move the position before the beginning of the stream.");
        }

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>
    /// The production registrations over the host doubles, with an auth service that knows one player token. With
    /// <paramref name="spoolDirectory"/>, the upload service the controller resolves spools there instead of the
    /// machine-wide directory (the service's spool seam is an init property, so it is registered on top).
    /// <paramref name="controllers"/> defaults to the temporary-map controller alone.
    /// </summary>
    private static async Task<LoopbackMvcHost> StartHostAsync(
        ScriptedHttpHandler handler, string spoolDirectory = null, Action<IApplicationBuilder> configureApp = null, Type[] controllers = null)
    {
        var authService = new Mock<IW3CAuthenticationService>(MockBehavior.Strict);
        authService.Setup(s => s.GetUserByToken(PlayerToken, false)).Returns(new W3CUserAuthenticationDto { BattleTag = BattleTag });
        var activitySource = new ActivitySource(nameof(TemporaryMapsControllerPipelineTests));
        return await LoopbackMvcHost.StartAsync(
            services =>
            {
                AddHostDoubles(services, handler, activitySource, authService.Object).AddMapServices();
                // The daily sweep is not under test here: started with the host it would call the scripted handler
                // (every round trip asserts what the handler saw) and purge the machine-wide spool directory.
                services.Remove(services.Single(d => d.ImplementationType == typeof(TemporaryMapExpiryService)));
                if (spoolDirectory != null)
                {
                    services.AddSingleton(provider => new TemporaryMapUploadService(
                        provider.GetRequiredService<MatchmakingServiceClient>(),
                        provider.GetRequiredService<UpdateServiceClient>(),
                        provider.GetRequiredService<MintRateLimiter>(),
                        provider.GetRequiredService<TemporaryMapFileKeyLock>(),
                        provider.GetRequiredService<ILogger<TemporaryMapUploadService>>())
                    {
                        SpoolDirectory = spoolDirectory,
                    });
                }
            },
            configureApp,
            controllers ?? [typeof(TemporaryMapsController)]);
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
