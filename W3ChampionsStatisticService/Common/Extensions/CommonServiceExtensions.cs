using Microsoft.Extensions.DependencyInjection;
using W3C.Domain.Common.Repositories;
using W3C.Domain.Common.Services;
using W3ChampionsStatisticService.Common.Repositories;
using W3ChampionsStatisticService.Common.Services;
using W3ChampionsStatisticService.Extensions;

namespace W3ChampionsStatisticService.Common.Extensions;

public static class CommonServiceExtensions
{
    public static IServiceCollection AddCommonServices(this IServiceCollection services)
    {
        // Common repositories
        services.AddInterceptedScoped<IAuditLogRepository, AuditLogRepository>();

        // Common services
        services.AddInterceptedTransient<IAuditLogService, AuditLogService>();
        services.AddInterceptedTransient<IOptimisticConcurrencyService, OptimisticConcurrencyService>();

        return services;
    }
}