using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Extensions;
using W3ChampionsStatisticService.NetworkDiagnostics.Delivery;
using W3ChampionsStatisticService.NetworkDiagnostics.GeoIp;
using W3ChampionsStatisticService.NetworkDiagnostics.Persistence;
using W3ChampionsStatisticService.NetworkDiagnostics.Runs;

namespace W3ChampionsStatisticService.NetworkDiagnostics;

public static class NetworkDiagnosticsServiceCollectionExtensions
{
    public static IServiceCollection AddNetworkDiagnostics(this IServiceCollection services)
    {
        services.AddInterceptedTransient<INetworkDiagnosticsRunRepository, NetworkDiagnosticsRunRepository>();
        services.AddInterceptedTransient<INetworkDiagnosticsAssignmentRepository, NetworkDiagnosticsAssignmentRepository>();
        services.AddInterceptedTransient<INetworkDiagnosticsResultRepository, NetworkDiagnosticsResultRepository>();
        services.AddInterceptedTransient<IRequiresIndexes, NetworkDiagnosticsRunRepository>();
        services.AddInterceptedTransient<IRequiresIndexes, NetworkDiagnosticsAssignmentRepository>();
        services.AddInterceptedTransient<IRequiresIndexes, NetworkDiagnosticsResultRepository>();

        // Built eagerly: a malformed NETDIAG_CORE_SERVERS / NETDIAG_BASELINES stops startup with a clear message.
        var targetsConfig = NetDiagTargetsConfig.FromEnvironment(Environment.GetEnvironmentVariable);
        services.AddSingleton(targetsConfig);
        services.TryAddSingleton(TimeProvider.System);
        // Missing GeoLite2 files must never fail startup: the lookup opens them lazily and keeps rechecking.
        services.AddSingleton<IGeoIpLookup>(sp => new MaxMindGeoIpLookup(
            targetsConfig.GeoIpCityDbPath, targetsConfig.GeoIpAsnDbPath,
            sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ILogger<MaxMindGeoIpLookup>>()));
        services.AddTransient<IFloNodeSource, MatchmakingFloNodeSource>();
        services.AddSingleton<IRandom, SystemRandom>();
        // Plain AddSingleton, no tracing interception: this service handles measurement tokens (A10).
        services.AddSingleton<INetworkDiagnosticsDelivery, NetworkDiagnosticsDelivery>();
        // Plain AddSingleton as well (tokens). The optional TimeSpan? ctor parameter needs a factory.
        services.AddSingleton<INetworkDiagnosticsScheduler, NetworkDiagnosticsScheduler>();
        services.AddHostedService(sp => new NetworkDiagnosticsDispatcher(
            sp.GetRequiredService<INetworkDiagnosticsScheduler>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<NetworkDiagnosticsDispatcher>>()));
        return services;
    }
}
