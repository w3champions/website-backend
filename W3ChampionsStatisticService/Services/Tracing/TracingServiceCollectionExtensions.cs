using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System;
using System.Diagnostics;
using System.Linq;
using W3ChampionsStatisticService.Services.Tracing.Sampling;
using W3ChampionsStatisticService.Services.Interceptors;
using W3ChampionsStatisticService.Filters;
using System.Reflection;
using OpenTelemetry.Exporter;
using MongoDB.Driver;
using MongoDB.Driver.Core.Extensions.DiagnosticSources;
using Microsoft.ApplicationInsights.Extensibility;
using W3ChampionsStatisticService.RateLimiting.Models;

namespace W3ChampionsStatisticService.Services.Tracing;

public static class TracingServiceCollectionExtensions
{
    const double TRACING_DEFAULT_SAMPLING_RATE = 0.01;
    const string TRACING_FARO_SESSION_ID_HTTP_HEADER = "x-faro-session-id";
    static readonly string OTEL_SERVICE_NAME = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") ?? "website-backend-undefined";
    static readonly string OTEL_EXPORTER_OTLP_ENDPOINT = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") ?? "http://localhost:4317";
    static readonly string OTEL_EXPORTER_OTLP_PROTOCOL = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL") ?? "Grpc";
    static readonly string SERVICE_VERSION = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "undefined";
    // MongoDbRepositoryBase names every collection after its document type.
    static readonly string API_TOKEN_COLLECTION_NAME = typeof(ApiToken).Name;
    public static IServiceCollection AddW3CTracing(
        this IServiceCollection services,
        string websiteBackendHubPath,
        MongoClientSettings mongoClientSettings)
    {
        mongoClientSettings.ClusterConfigurator = cb => cb.Subscribe(new DiagnosticsActivityEventSubscriber(CreateMongoInstrumentationOptions()));
        mongoClientSettings.ApplicationName = OTEL_SERVICE_NAME;

        services.AddSingleton(new ActivitySource(OTEL_SERVICE_NAME));

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(OTEL_SERVICE_NAME, serviceVersion: SERVICE_VERSION))
            .WithTracing(tracing => tracing
                .SetSampler(new ParentBasedSampler(new CustomRootSampler(TRACING_DEFAULT_SAMPLING_RATE)))
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.Filter = context =>
                    {
                        if (HttpMethods.IsOptions(context.Request.Method))
                        {
                            return false;
                        }
                        if (context.Request.Path.Equals(websiteBackendHubPath))
                        {
                            return false;
                        }
                        return true;
                    };
                    options.EnrichWithHttpRequest = (activity, httpRequest) =>
                    {
                        if (httpRequest.Headers.TryGetValue(TRACING_FARO_SESSION_ID_HTTP_HEADER, out var faroSessionIdValues))
                        {
                            var faroSessionId = faroSessionIdValues.FirstOrDefault();
                            if (!string.IsNullOrEmpty(faroSessionId))
                            {
                                activity.SetTag(BaggageToTagProcessor.SessionIdKey, faroSessionId);
                                activity.AddBaggage(BaggageToTagProcessor.SessionIdKey, faroSessionId);
                            }
                        }
                    };
                })
                .AddHttpClientInstrumentation(options =>
                {
                    options.FilterHttpRequestMessage = request =>
                    {
                        // Instance-metadata endpoint (AWS, etc)
                        return request.RequestUri?.Host != "169.254.169.254";
                    };
                    options.EnrichWithHttpRequestMessage = (activity, httpRequestMessage) =>
                    {
                        var faroSessionIdFromBaggage = activity.GetBaggageItem(BaggageToTagProcessor.SessionIdKey);
                        if (!string.IsNullOrEmpty(faroSessionIdFromBaggage))
                        {
                            httpRequestMessage.Headers.TryAddWithoutValidation(TRACING_FARO_SESSION_ID_HTTP_HEADER, faroSessionIdFromBaggage);
                        }
                    };
                })
                .AddSource("MongoDB.Driver.Core.Extensions.DiagnosticSources")
                .AddSource(OTEL_SERVICE_NAME)
                .AddProcessor(new BaggageToTagProcessor())
                // Before the exporter: strips proofHash values from URL attributes (spec §10.3).
                .AddProcessor(new TelemetryRedactionProcessor())
                .AddOtlpExporter(options =>
                {
                    options.Endpoint = new Uri(OTEL_EXPORTER_OTLP_ENDPOINT);
                    options.Protocol = Enum.Parse<OtlpExportProtocol>(OTEL_EXPORTER_OTLP_PROTOCOL);
                })
            );

        // Add core tracing services
        services.AddSingleton<TracingService>();
        services.AddSingleton<TracingInterceptor>();
        services.AddTransient<SignalRTraceContextFilter>();

        return services;
    }

    /// <summary>
    /// Application Insights, with <see cref="TelemetryRedactionInitializer"/> so request and dependency URLs never
    /// carry a proofHash (spec §10.3).
    /// </summary>
    public static IServiceCollection AddW3CApplicationInsights(this IServiceCollection services, string appInsightsKey)
    {
        services.AddApplicationInsightsTelemetry(c => c.ConnectionString = "InstrumentationKey=" + appInsightsKey?.Replace("'", ""));
        services.AddSingleton<ITelemetryInitializer, TelemetryRedactionInitializer>();
        return services;
    }

    /// <summary>
    /// Command text stays captured for every collection except the API-token one: its lookups and last-used updates
    /// filter on the raw token, which would otherwise sit in <c>db.query.text</c> of every sampled span.
    /// </summary>
    internal static InstrumentationOptions CreateMongoInstrumentationOptions() => new()
    {
        CaptureCommandText = true,
        ShouldStartActivity = command => !string.Equals(command.GetCollectionName(), API_TOKEN_COLLECTION_NAME, StringComparison.Ordinal),
    };
}
