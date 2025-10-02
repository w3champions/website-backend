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

namespace W3ChampionsStatisticService.Services.Tracing;

public static class TracingServiceCollectionExtensions
{
    const double TRACING_DEFAULT_SAMPLING_RATE = 0.01;
    const string TRACING_FARO_SESSION_ID_HTTP_HEADER = "x-faro-session-id";
    static readonly string OTEL_SERVICE_NAME = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") ?? "website-backend-undefined";
    static readonly string OTEL_EXPORTER_OTLP_ENDPOINT = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") ?? "http://localhost:4317";
    static readonly string OTEL_EXPORTER_OTLP_PROTOCOL = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL") ?? "Grpc";
    static readonly string SERVICE_VERSION = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "undefined";
    public static IServiceCollection AddW3CTracing(
        this IServiceCollection services,
        string websiteBackendHubPath,
        MongoClientSettings mongoClientSettings)
    {
        mongoClientSettings.ClusterConfigurator = cb => cb.Subscribe(new DiagnosticsActivityEventSubscriber(new InstrumentationOptions { CaptureCommandText = true }));
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
}
