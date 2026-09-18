using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Configuration;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Events;
using MongoDB.Driver.Core.Servers;
using NUnit.Framework;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Trace;
using Serilog;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Services.Tracing;
using WC3ChampionsStatisticService.Tests.Maps;

namespace WC3ChampionsStatisticService.Tests.Tracing;

/// <summary>
/// Runs the real <see cref="TracingServiceCollectionExtensions.AddW3CTracing"/> pipeline (spec §10.3): what a
/// span carries when its OTLP exporter sends it, and which Mongo commands start a span at all.
/// </summary>
[TestFixture]
public class TracingPipelineRedactionTests
{
    private const string MongoSourceName = "MongoDB.Driver.Core.Extensions.DiagnosticSources";
    private const string TestDatabase = "w3c-telemetry-redaction-tests";
    private const string ApiTokenValue = "raw-api-token-value-under-test";

    [Test]
    public async Task OutboundProofHashPathSegment_IsRedactedBeforeTheOtlpExporterSendsTheSpan()
    {
        // The HttpClient instrumentation redacts query VALUES only; the matchmaking by-proof-hash route
        // carries the proofHash as a PATH segment, which it records verbatim in url.full.
        var exported = await ExportedBytesOfOutboundCalls($"maps/temporary/by-proof-hash/{TemporaryMapClientTests.ProofHash}");

        Assert.That(exported, Does.Contain("/maps/temporary/by-proof-hash/Redacted"));
        Assert.That(exported, Does.Not.Contain(TemporaryMapClientTests.ProofHash));
    }

    [Test]
    public async Task OutboundCredentialQueryValues_NeverReachTheOtlpExporter()
    {
        // ReplayServiceClient's ?secret= and IdentityServiceClient's ?authorization= shapes.
        var exported = await ExportedBytesOfOutboundCalls(
            $"generate/42?secret={TelemetryRedactionTests.OutboundSecret}",
            $"api/permissions?id=Peter%23123&authorization={TelemetryRedactionTests.OutboundJwt}");

        Assert.That(exported, Does.Contain("/generate/42?secret=Redacted"));
        Assert.That(exported, Does.Contain("authorization=Redacted"));
        Assert.That(exported, Does.Not.Contain(TelemetryRedactionTests.OutboundSecret));
        Assert.That(exported, Does.Not.Contain(TelemetryRedactionTests.OutboundJwt));
    }

    [Test]
    public async Task OutboundProofHashBody_AndAdminSecretHeader_NeverReachTheOtlpExporter()
    {
        // Revision 10: the by-proof-hash lookup carries the proofHash in a JSON body and the admin secret in a
        // header. The HttpClient instrumentation records neither bodies nor headers, and nothing in AddW3CTracing
        // turns either on; the URL it does record has nothing left to redact.
        var exported = await ExportedBytesOfOutboundCalls(baseUrl =>
        {
            var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "maps/temporary/by-proof-hash")
            {
                Content = new StringContent("{\"proofHash\":\"" + TemporaryMapClientTests.ProofHash + "\"}", Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation("x-admin-secret", TelemetryRedactionTests.OutboundSecret);
            return request;
        });

        Assert.That(exported, Does.Contain("/maps/temporary/by-proof-hash"));
        Assert.That(exported, Does.Not.Contain(TemporaryMapClientTests.ProofHash));
        Assert.That(exported, Does.Not.Contain(TelemetryRedactionTests.OutboundSecret));
    }

    [Test]
    public async Task HttpClientFactoryTraceLogging_PrintsEveryHeaderValueAsAnAsterisk_AndNeverTheBody()
    {
        // IHttpClientFactory's logging handlers list the request headers at Trace, a level the logger configuration
        // keeps off for that category (LogLevelOverrideTests). Microsoft.Extensions.Http 9 prints every header value
        // as "*" unless RedactLoggedHeaders narrows that (nothing here does), and no level logs a body: the admin
        // secret and the revision-10 proofHash body of the real matchmaking client, over the real factory and a
        // scripted transport, stay out of the log even with the category wide open.
        var sink = new CapturingLogSink();
        using var serilog = sink.CreateLogger();
        var handler = new ScriptedHttpHandler().On(HttpMethod.Post, "/maps/temporary/by-proof-hash", HttpStatusCode.OK, "{\"fileState\":\"present\"}");
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddSerilog(serilog));
        services.AddHttpClient();
        services.ConfigureHttpClientDefaults(client => client.ConfigurePrimaryHttpMessageHandler(() => handler));
        await using var provider = services.BuildServiceProvider();
        var client = new MatchmakingServiceClient(provider.GetRequiredService<IHttpClientFactory>());

        var state = await client.GetTemporaryMapStateByProofHash(TemporaryMapClientTests.ProofHash);

        Assert.That(state.FileState, Is.EqualTo("present"));
        var adminSecret = handler.Requests.Single().Headers.GetValues("x-admin-secret").Single();
        var lines = sink.Events.Select(e => e.RenderMessage()).ToList();
        Assert.That(lines.Any(l => l.Contains("x-admin-secret: *")), Is.True, "the header log was not written, or the secret not masked");
        Assert.That(lines.Any(l => l.Contains("/maps/temporary/by-proof-hash")), Is.True, "method and URL are logged as before");
        // Boolean checks: a failure must not print the secret or the proofHash.
        Assert.That(lines.Any(l => l.Contains(adminSecret)), Is.False, "the admin secret was logged");
        Assert.That(lines.Any(l => l.Contains(TemporaryMapClientTests.ProofHash)), Is.False, "the proofHash was logged");
    }

    [Test]
    public void RedactionProcessor_RunsBeforeTheExporter()
    {
        // A simple export processor exports synchronously in OnEnd, so a redaction registered after it would edit
        // a span that has already left. The exporter snapshots tag values at export time; AddW3CTracing registers
        // its OTLP exporter through the same helper.
        var exporter = new SnapshotExporter();
        var sourceName = "w3c-redaction-order-test-" + Guid.NewGuid().ToString("N");
        using var source = new ActivitySource(sourceName);
        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .SetSampler(new AlwaysOnSampler())
            .AddW3CProcessorsThenExporter(tracing => tracing.AddProcessor(new SimpleActivityExportProcessor(exporter)))
            .Build();

        using (var activity = source.StartActivity("GET", ActivityKind.Client))
        {
            activity!.SetTag("url.full", $"https://mm.test/maps/temporary/by-proof-hash/{TemporaryMapClientTests.ProofHash}");
        }

        var exported = exporter.Exported.Single();
        Assert.That(exported["url.full"], Is.EqualTo("https://mm.test/maps/temporary/by-proof-hash/Redacted"));
    }

    [Test]
    public void ApiTokenCommands_StartNoSpan_WhileOtherCommandsKeepTheirCommandText()
    {
        var settings = new MongoClientSettings();
        new ServiceCollection().AddW3CTracing("/hub", settings);
        var onCommandStarted = CommandStartedHandlerConfiguredBy(settings);

        var started = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == MongoSourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = started.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);

        var previous = Activity.Current;
        try
        {
            onCommandStarted(Command("find", 1, new BsonDocument
            {
                { "find", "ApiToken" },
                { "filter", new BsonDocument { { "Token", ApiTokenValue }, { "IsActive", true } } },
            }));
            onCommandStarted(Command("update", 2, new BsonDocument
            {
                { "update", "ApiToken" },
                { "updates", new BsonArray { new BsonDocument { { "q", new BsonDocument("Token", ApiTokenValue) } } } },
            }));
            onCommandStarted(Command("getMore", 3, new BsonDocument { { "getMore", 42L }, { "collection", "ApiToken" } }));
            onCommandStarted(Command("find", 4, new BsonDocument
            {
                { "find", "Clan" },
                { "filter", new BsonDocument("ClanId", "clan-filter-marker") },
            }));

            var spans = started.Where(a => a.TagObjects.Any(t => Equals(t.Value, TestDatabase))).ToList();
            Assert.That(spans.SelectMany(a => a.TagObjects).Any(t => t.Value?.ToString()?.Contains(ApiTokenValue) == true), Is.False,
                "an API-token command's text reached a span");
            Assert.That(spans, Has.Count.EqualTo(1), "only the Clan command may start a span");
            Assert.That(spans[0].TagObjects.Any(t => t.Value?.ToString()?.Contains("clan-filter-marker") == true), Is.True,
                "command-text capture must stay on for every other collection");
        }
        finally
        {
            foreach (var activity in started.Reverse())
            {
                activity.Stop();
            }
            Activity.Current = previous;
        }
    }

    private static Action<CommandStartedEvent> CommandStartedHandlerConfiguredBy(MongoClientSettings settings)
    {
        var builder = new ClusterBuilder();
        settings.ClusterConfigurator(builder);

        // ClusterBuilder keeps every Subscribe()d subscriber behind one aggregator and exposes no getter.
        var aggregatorField = typeof(ClusterBuilder).GetField("_eventAggregator", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(aggregatorField, Is.Not.Null, "MongoDB.Driver's ClusterBuilder no longer has _eventAggregator; update this test");
        var aggregator = (IEventSubscriber)aggregatorField!.GetValue(builder);
        Assert.That(aggregator!.TryGetEventHandler<CommandStartedEvent>(out var handler), Is.True,
            "AddW3CTracing subscribed no CommandStartedEvent handler");
        return handler;
    }

    private static CommandStartedEvent Command(string name, int requestId, BsonDocument command)
        => new(name, command, new DatabaseNamespace(TestDatabase), null, requestId,
            new ConnectionId(new ServerId(new ClusterId(), new DnsEndPoint("localhost", 27017))));

    private sealed class SnapshotExporter : BaseExporter<Activity>
    {
        public ConcurrentQueue<Dictionary<string, string>> Exported { get; } = new();

        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (var activity in batch)
            {
                Exported.Enqueue(activity.TagObjects.ToDictionary(t => t.Key, t => t.Value?.ToString()));
            }

            return ExportResult.Success;
        }
    }

    /// <summary>
    /// Sends one GET per path through <see cref="TracingServiceCollectionExtensions.AddW3CTracing"/> exactly as
    /// registered and returns everything its OTLP exporter sent, decoded as Latin-1 so ASCII attribute values can be
    /// searched. Production exports in batches, where a wrong processor order would only lose a race; the same
    /// exporter switched to simple export sends each span synchronously when it ends, in registration order, so a
    /// redaction registered after it fails here. Its HttpClient answers in memory: nothing leaves the process.
    /// </summary>
    private static Task<string> ExportedBytesOfOutboundCalls(params string[] paths)
        => ExportedBytesOfOutboundCalls(paths.Select(path => new Func<string, HttpRequestMessage>(
            baseUrl => new HttpRequestMessage(HttpMethod.Get, baseUrl + path))).ToArray());

    /// <summary>As above, for requests other than a bare GET: each builder gets the server's base URL.</summary>
    private static async Task<string> ExportedBytesOfOutboundCalls(params Func<string, HttpRequestMessage>[] requests)
    {
        var collector = new InMemoryOtlpCollector();
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<OtlpExporterOptions>(options =>
        {
            options.ExportProcessorType = ExportProcessorType.Simple;
            options.HttpClientFactory = () => new HttpClient(collector);
        });
        services.AddW3CTracing("/hub", new MongoClientSettings());
        var provider = services.BuildServiceProvider();
        var tracerProvider = provider.GetRequiredService<TracerProvider>();

        try
        {
            using var server = LoopbackServer.Start();
            using (var client = new HttpClient())
            {
                foreach (var request in requests)
                {
                    using var response = await client.SendAsync(request(server.BaseUrl));
                    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
                }
            }

            Assert.That(collector.Requests, Has.Count.EqualTo(requests.Length), "the OTLP exporter did not send one request per span");
            return string.Concat(collector.Requests.Select(Encoding.Latin1.GetString));
        }
        finally
        {
            tracerProvider.Shutdown(1000);
            await provider.DisposeAsync();
        }
    }

    /// <summary>An HTTP server on a free loopback port, answering every request with matchmaking's record-not-found 404 {}.</summary>
    private sealed class LoopbackServer : IDisposable
    {
        private readonly HttpListener _listener;

        private LoopbackServer(HttpListener listener, string baseUrl)
        {
            _listener = listener;
            BaseUrl = baseUrl;
        }

        public string BaseUrl { get; }

        public static LoopbackServer Start()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            var baseUrl = $"http://127.0.0.1:{port}/";
            var listener = new HttpListener();
            listener.Prefixes.Add(baseUrl);
            listener.Start();
            _ = Task.Run(async () =>
            {
                while (listener.IsListening)
                {
                    HttpListenerContext context;
                    try
                    {
                        context = await listener.GetContextAsync();
                    }
                    catch (Exception) when (!listener.IsListening)
                    {
                        return;
                    }

                    context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                    var body = "{}"u8.ToArray();
                    await context.Response.OutputStream.WriteAsync(body);
                    context.Response.Close();
                }
            });
            return new LoopbackServer(listener, baseUrl);
        }

        public void Dispose() => _listener.Close();
    }
}
