using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.Extensibility.Implementation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Moq;
using NUnit.Framework;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Instrumentation.AspNetCore;
using Serilog;
using W3C.Domain.Maps;
using W3ChampionsStatisticService.Maps;
using W3ChampionsStatisticService.Services.BackgroundTasks;
using W3ChampionsStatisticService.Services.Tracing;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using WC3ChampionsStatisticService.Tests.Maps;
using WC3ChampionsStatisticService.Tests.WebApi;

namespace WC3ChampionsStatisticService.Tests.Tracing;

/// <summary>
/// A real pre-check request (spec A.4, revision 10: the proofHash travels in the x-proof-hash header) through Kestrel,
/// the production tracing pipeline (<see cref="TracingServiceCollectionExtensions.AddW3CTracing"/>), Application
/// Insights request tracking with <see cref="TelemetryRedactionInitializer"/>, and every log category at Trace: what
/// the OTLP exporter sends, what the Application Insights channel receives and what is logged are searched for the
/// header's value (spec §10.3). Nothing in production records request headers; the second fixture adds hooks that
/// do, so the redaction of the semantic-convention header attributes is exercised end to end.
/// </summary>
[TestFixture]
public class InboundHeaderRedactionPipelineTests : TemporaryMapUploadServiceTestBase
{
    private const string PlayerToken = "player-token-under-test";
    private const string MarkerHeader = "x-test-marker";
    private const string MarkerValue = "marker-value-passes-through";
    private const string HeaderTagPrefix = "http.request.header.";
    private const string ProofHashHeaderTag = HeaderTagPrefix + TemporaryMapKeys.ProofHashHeaderName;

    [Test]
    public async Task AStatusRequest_RecordsNoHeader_AndNothingCarriesTheProofHash()
    {
        var telemetry = await TelemetryOfAStatusRequest(recordHeaders: false);

        Assert.That(telemetry.Exported, Does.Contain("/api/maps/temporary/status"), "the span was exported");
        Assert.That(telemetry.Exported, Does.Not.Contain(HeaderTagPrefix), "the production pipeline records no request header");
        Assert.That(telemetry.Exported, Does.Not.Contain(MarkerValue));
        Assert.That(telemetry.AppInsights, Does.Contain("/api/maps/temporary/status"), "the request telemetry was tracked");
        Assert.That(telemetry.AppInsights, Does.Not.Contain(MarkerValue));
        Assert.That(telemetry.LogLines, Is.Not.Empty, "Trace-level logging captured nothing; the check would be vacuous");
        telemetry.AssertNothingCarries(ProofHash, "proofHash");
        telemetry.AssertNothingCarries(PlayerToken, "player token");
    }

    [Test]
    public async Task AStatusRequest_WhoseHeadersHooksRecord_LeavesWithTheCredentialHeadersRedacted()
    {
        // An enrich hook tagging every request header, and an initializer copying them into the request telemetry's
        // properties, as a future change or a header-capturing option would: the marker header passes through
        // verbatim, the x-proof-hash and Authorization values do not.
        var telemetry = await TelemetryOfAStatusRequest(recordHeaders: true);

        Assert.That(telemetry.Exported, Does.Contain(ProofHashHeaderTag), "the header attribute itself is kept");
        Assert.That(telemetry.Exported, Does.Contain(HeaderTagPrefix + "authorization"));
        Assert.That(telemetry.Exported, Does.Contain(TelemetryRedaction.Redacted));
        Assert.That(telemetry.Exported, Does.Contain(MarkerValue), "a header that is no credential is recorded as sent");
        Assert.That(telemetry.AppInsights, Does.Contain(ProofHashHeaderTag));
        Assert.That(telemetry.AppInsights, Does.Contain(TelemetryRedaction.Redacted));
        Assert.That(telemetry.AppInsights, Does.Contain(MarkerValue));
        telemetry.AssertNothingCarries(ProofHash, "proofHash");
        telemetry.AssertNothingCarries(PlayerToken, "player token");
    }

    /// <summary>
    /// Runs one pre-check with a valid header through the host and returns what its OTLP exporter sent (decoded as
    /// Latin-1 so ASCII attribute values can be searched), the Application Insights items as the channel would
    /// serialise them, and every log event as one line. The request carries a sampled traceparent: root server spans
    /// are sampled at 1%, and an unsampled span exports nothing.
    /// </summary>
    private async Task<CapturedTelemetry> TelemetryOfAStatusRequest(bool recordHeaders)
    {
        var collector = new InMemoryOtlpCollector();
        var channel = new CollectingTelemetryChannel();
        var sink = new CapturingLogSink();
        using var serilog = sink.CreateLogger();
        var handler = new ScriptedHttpHandler().On(HttpMethod.Post, "/maps/temporary/by-proof-hash", HttpStatusCode.OK, "{\"fileState\":\"present\"}");
        var authService = new Mock<IW3CAuthenticationService>(MockBehavior.Strict);
        authService.Setup(s => s.GetUserByToken(PlayerToken, false)).Returns(new W3CUserAuthenticationDto { BattleTag = BattleTag });

        await using var host = await LoopbackMvcHost.StartAsync(
            services =>
            {
                AddHostDoubles(services, handler, new ActivitySource(nameof(InboundHeaderRedactionPipelineTests)), authService.Object).AddMapServices();
                services.Remove(services.Single(d => d.ImplementationType == typeof(TemporaryMapExpiryService)));
                services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddSerilog(serilog));
                services.Configure<OtlpExporterOptions>(options =>
                {
                    options.ExportProcessorType = ExportProcessorType.Simple;
                    options.HttpClientFactory = () => new HttpClient(collector);
                });
                services.AddW3CTracing("/hub", new MongoClientSettings());
                services.AddHttpContextAccessor();
                services.AddSingleton<ITelemetryChannel>(channel);
                if (recordHeaders)
                {
                    services.Configure<AspNetCoreTraceInstrumentationOptions>(options =>
                    {
                        var configured = options.EnrichWithHttpRequest;
                        options.EnrichWithHttpRequest = (activity, request) =>
                        {
                            configured?.Invoke(activity, request);
                            foreach (var header in request.Headers)
                            {
                                activity.SetTag(HeaderTagPrefix + header.Key.ToLowerInvariant(), header.Value.ToString());
                            }
                        };
                    });
                    // Before AddW3CApplicationInsights: initializers run in registration order.
                    services.AddSingleton<ITelemetryInitializer, HeaderCopyingInitializer>();
                }

                services.AddW3CApplicationInsights("00000000-0000-0000-0000-000000000000");
                services.Configure<ApplicationInsightsServiceOptions>(options =>
                {
                    // Request tracking only: no module may start background work or reach the network from a test.
                    options.EnableQuickPulseMetricStream = false;
                    options.EnablePerformanceCounterCollectionModule = false;
                    options.EnableAppServicesHeartbeatTelemetryModule = false;
                    options.EnableAzureInstanceMetadataTelemetryModule = false;
                    options.EnableDependencyTrackingTelemetryModule = false;
                    options.EnableEventCounterCollectionModule = false;
                    options.EnableDiagnosticsTelemetryModule = false;
                    options.EnableHeartbeat = false;
                    options.EnableAdaptiveSampling = false;
                    options.AddAutoCollectedMetricExtractor = false;
                });
            },
            typeof(TemporaryMapsController));

        // Application Insights initialises its modules with the configuration; a real host does this at startup.
        host.Services.GetRequiredService<TelemetryConfiguration>();
        var request = new HttpRequestMessage(HttpMethod.Get, "api/maps/temporary/status");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", PlayerToken);
        request.Headers.TryAddWithoutValidation(TemporaryMapKeys.ProofHashHeaderName, ProofHash);
        request.Headers.TryAddWithoutValidation(MarkerHeader, MarkerValue);
        request.Headers.TryAddWithoutValidation("traceparent", "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01");

        using var response = await host.Client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("{\"state\":\"ready\"}"));
        // The server span ends, and the request telemetry is tracked, after the response has left; the client span
        // of host.Client (the HttpClient instrumentation listens process-wide) is exported too, and carries no route.
        // The exports are decoded again only when another one has arrived.
        var exported = "";
        var exportsDecoded = 0;
        await WaitUntilAsync(() =>
        {
            if (collector.Requests.Count != exportsDecoded)
            {
                exportsDecoded = collector.Requests.Count;
                exported = string.Concat(collector.Requests.Select(Encoding.Latin1.GetString));
            }

            return exported.Contains("http.route") && channel.Items.OfType<RequestTelemetry>().Any();
        }, "the server span was not exported or the request telemetry not tracked");

        return new CapturedTelemetry(
            exported,
            Encoding.UTF8.GetString(JsonSerializer.Serialize(channel.Items.ToArray(), compress: false)),
            sink.Events
                .Select(e => $"{e.RenderMessage()} | {string.Join(" | ", e.Properties.Select(p => $"{p.Key}={p.Value}"))} | {e.Exception}")
                .ToArray());
    }

    private sealed record CapturedTelemetry(string Exported, string AppInsights, string[] LogLines)
    {
        /// <summary>Boolean checks only: a failure names what leaked, never where or the value itself.</summary>
        public void AssertNothingCarries(string secret, string what)
        {
            Assert.That(Exported.Contains(secret, StringComparison.Ordinal), Is.False, $"the {what} reached the OTLP exporter");
            Assert.That(AppInsights.Contains(secret, StringComparison.Ordinal), Is.False, $"the {what} reached the Application Insights channel");
            Assert.That(LogLines.Any(line => line.Contains(secret, StringComparison.Ordinal)), Is.False, $"the {what} was logged");
        }
    }

    /// <summary>What a header-dumping initializer would do: every request header into the request's properties.</summary>
    private sealed class HeaderCopyingInitializer(IHttpContextAccessor httpContextAccessor) : ITelemetryInitializer
    {
        public void Initialize(ITelemetry telemetry)
        {
            if (telemetry is not RequestTelemetry request || httpContextAccessor.HttpContext is not { } context)
            {
                return;
            }

            foreach (var header in context.Request.Headers)
            {
                request.Properties[HeaderTagPrefix + header.Key.ToLowerInvariant()] = header.Value.ToString();
            }
        }
    }
}
