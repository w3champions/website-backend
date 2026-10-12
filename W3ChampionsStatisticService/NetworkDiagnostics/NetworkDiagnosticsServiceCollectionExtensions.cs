using Microsoft.Extensions.DependencyInjection;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Extensions;
using W3ChampionsStatisticService.NetworkDiagnostics.Persistence;

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
        return services;
    }
}
