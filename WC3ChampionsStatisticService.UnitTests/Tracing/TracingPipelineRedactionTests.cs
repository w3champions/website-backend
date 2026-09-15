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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
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
    private static async Task<string> ExportedBytesOfOutboundCalls(params string[] paths)
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
                foreach (var path in paths)
                {
                    using var response = await client.GetAsync(server.BaseUrl + path);
                    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
                }
            }

            Assert.That(collector.Requests, Has.Count.EqualTo(paths.Length), "the OTLP exporter did not send one request per span");
            return string.Concat(collector.Requests.Select(Encoding.Latin1.GetString));
        }
        finally
        {
            tracerProvider.Shutdown(1000);
            await provider.DisposeAsync();
        }
    }

    /// <summary>Stands in for an OTLP collector (gRPC or HTTP/protobuf): records each export body and accepts it.</summary>
    private sealed class InMemoryOtlpCollector : HttpMessageHandler
    {
        public ConcurrentQueue<byte[]> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Enqueue(await request.Content!.ReadAsByteArrayAsync(cancellationToken));
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
            response.Headers.TryAddWithoutValidation("grpc-status", "0");
            return response;
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
