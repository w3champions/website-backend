using Microsoft.Extensions.DependencyInjection;
using W3ChampionsStatisticService.Extensions;
using W3ChampionsStatisticService.Services.BackgroundTasks;
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

        // The per-fileKey lock (R-I1/S-H1, S6-M1). ONE instance per process is load-bearing: the upload service and the
        // expiry sweep serialise every store, record write, probe and delete of one fileKey on it. Plain AddSingleton
        // like MintRateLimiter: infra state, not a traced service.
        services.AddSingleton<TemporaryMapFileKeyLock>();

        // The §6.3 orchestration, next to the singleton clients it drives.
        services.AddInterceptedSingleton<TemporaryMapUploadService>();

        // In-flight upload bounds (D7 + the process-wide cap). ONE instance per process is load-bearing, and plain
        // AddSingleton (no tracing interception) like MintRateLimiter: infra state, not a traced service.
        services.AddSingleton<TemporaryMapUploadGate>();

        // The §3.5 expiry and reconciliation sweep. ONE instance per process: its run lock is what keeps the daily run
        // and an admin-triggered run (TemporaryMapExpiryJob, registered with the other jobs in AddAdminJobs) apart.
        services.AddInterceptedSingleton<TemporaryMapExpirySweep>();

        // The daily trigger of that sweep, next to the other BackgroundServices Program.cs hosts.
        services.AddHostedService<TemporaryMapExpiryService>();

        return services;
    }
}
