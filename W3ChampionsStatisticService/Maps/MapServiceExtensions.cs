using Microsoft.Extensions.DependencyInjection;
using W3ChampionsStatisticService.Extensions;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.Maps;

/// <summary>
/// Registrations of the self-provided (temporary) custom-map feature, in one place so a test can resolve them
/// (D9, <c>MapServiceExtensionsTests</c>). Depends on what Program.cs registers before calling it: the two service
/// clients, the shared <see cref="Sessions.MintRateLimiter"/>, <see cref="IW3CAuthenticationService"/>, logging and
/// the tracing interceptor.
/// </summary>
public static class MapServiceExtensions
{
    public static IServiceCollection AddMapServices(this IServiceCollection services)
    {
        // The player auth filter of both temporary-map routes, resolved through BearerRequiresPlayerAuthAttribute.
        // Transient and traced like the other action filters registered in Program.cs.
        services.AddInterceptedTransient<BearerRequiresPlayerAuthFilter>();

        // The §6.3 orchestration, next to the singleton clients it drives.
        services.AddInterceptedSingleton<TemporaryMapUploadService>();

        // In-flight upload bounds (D7 + the process-wide cap). ONE instance per process is load-bearing, and plain
        // AddSingleton (no tracing interception) like MintRateLimiter: infra state, not a traced service.
        services.AddSingleton<TemporaryMapUploadGate>();

        return services;
    }
}
