using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System;
using System.Diagnostics;
using System.Linq;
using W3ChampionsStatisticService.Services.Tracing.Sampling;
using W3ChampionsStatisticService.Services.Interceptors;
using W3ChampionsStatisticService.Filters;

namespace W3ChampionsStatisticService.Services.Tracing;

public static class TracingServiceCollectionExtensions
{
    const double TRACING_DEFAULT_SAMPLING_RATE = 0.01;
    const string TRACING_FARO_SESSION_ID_HTTP_HEADER = "x-faro-session-id";
    public static IServiceCollection AddW3CTracing(
        this IServiceCollection services,
        string serviceName,
        string serviceVersion,
        string otlpEndpoint,
        string websiteBackendHubPath)
    {
        services.AddSingleton(new ActivitySource(serviceName));

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName, serviceVersion: serviceVersion))
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
                .AddSource(serviceName)
                .AddProcessor(new BaggageToTagProcessor())
                .AddOtlpExporter(options =>
                {
                    options.Endpoint = new Uri(otlpEndpoint);
                })
            );

        // Add core tracing services
        services.AddSingleton<TracingService>();
        services.AddSingleton<TracingInterceptor>();
        services.AddTransient<SignalRTraceContextFilter>();

        return services;
    }
}
