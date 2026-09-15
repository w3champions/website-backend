using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
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
using OpenTelemetry.Trace;
using W3ChampionsStatisticService.Services.Tracing;
using WC3ChampionsStatisticService.Tests.Maps;

namespace WC3ChampionsStatisticService.Tests.Tracing;

/// <summary>
/// Runs the real <see cref="TracingServiceCollectionExtensions.AddW3CTracing"/> pipeline (spec §10.3): what a
/// span carries when it reaches the exporter, and which Mongo commands start a span at all.
/// </summary>
[TestFixture]
public class TracingPipelineRedactionTests
{
    private const string MongoSourceName = "MongoDB.Driver.Core.Extensions.DiagnosticSources";
    private const string TestDatabase = "w3c-telemetry-redaction-tests";
    private const string ApiTokenValue = "raw-api-token-value-under-test";

    [Test]
    public async Task OutboundProofHashPathSegment_IsRedactedBeforeTheSpanIsExported()
    {
        // The HttpClient instrumentation redacts query VALUES only; the matchmaking by-proof-hash route
        // carries the proofHash as a PATH segment, which it records verbatim in url.full.
        var captured = new CapturingProcessor();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddW3CTracing("/hub", new MongoClientSettings());
        services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddProcessor(captured));
        var provider = services.BuildServiceProvider();
        var tracerProvider = provider.GetRequiredService<TracerProvider>();

        try
        {
            using var server = LoopbackServer.Start();
            using (var client = new HttpClient())
            {
                using var response = await client.GetAsync(
                    $"{server.BaseUrl}maps/temporary/by-proof-hash/{TemporaryMapClientTests.ProofHash}");
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            }

            var span = captured.Ended.SingleOrDefault(a => a.Kind == ActivityKind.Client && a.GetTagItem("url.full") != null);
            Assert.That(span, Is.Not.Null, "the HttpClient instrumentation recorded no client span");
            var urlFull = (string)span!.GetTagItem("url.full");
            Assert.That(urlFull, Does.Not.Contain(TemporaryMapClientTests.ProofHash));
            Assert.That(urlFull, Does.EndWith("/maps/temporary/by-proof-hash/Redacted"));
        }
        finally
        {
            tracerProvider.Shutdown(1000);
            await provider.DisposeAsync();
        }
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

    private sealed class CapturingProcessor : BaseProcessor<Activity>
    {
        public ConcurrentQueue<Activity> Ended { get; } = new();

        public override void OnEnd(Activity data) => Ended.Enqueue(data);
    }

    /// <summary>A one-request HTTP server on a free loopback port, answering matchmaking's record-not-found 404 {}.</summary>
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
                var context = await listener.GetContextAsync();
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                var body = "{}"u8.ToArray();
                await context.Response.OutputStream.WriteAsync(body);
                context.Response.Close();
            });
            return new LoopbackServer(listener, baseUrl);
        }

        public void Dispose() => _listener.Close();
    }
}
