using Microsoft.Extensions.DependencyInjection;
using W3ChampionsStatisticService.Extensions;

namespace W3ChampionsStatisticService.Admin.Jobs;

public static class AdminJobServiceExtensions
{
    public static IServiceCollection AddAdminJobs(this IServiceCollection services)
    {
        services.AddInterceptedScoped<IAdminJobRepository, AdminJobRepository>();

        // Singleton because it owns the cancellation tokens of everything running, and
        // a hosted service resolving to that same instance so shutdown reaches them.
        services.AddSingleton<AdminJobRunner>();
        services.AddHostedService(sp => sp.GetRequiredService<AdminJobRunner>());

        // Job registrations. Discovered by the runner and the controller as IAdminJob.
        return services;
    }
}
