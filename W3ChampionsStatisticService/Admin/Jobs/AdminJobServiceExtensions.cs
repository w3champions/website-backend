using Microsoft.Extensions.DependencyInjection;
using W3ChampionsStatisticService.Extensions;
using W3ChampionsStatisticService.Ladder;

namespace W3ChampionsStatisticService.Admin.Jobs;

public static class AdminJobServiceExtensions
{
    public static IServiceCollection AddAdminJobs(this IServiceCollection services)
    {
        services.AddInterceptedScoped<IAdminJobRepository, AdminJobRepository>();

        // Singleton so the "serverStatus is not permitted" warning is logged once for
        // the process rather than once per job run.
        services.AddSingleton<IPressureProbe, PressureProbe>();

        // Singleton because it owns the cancellation tokens of everything running, and
        // a hosted service resolving to that same instance so shutdown reaches them.
        services.AddSingleton<AdminJobRunner>();
        services.AddHostedService(sp => sp.GetRequiredService<AdminJobRunner>());

        // Job registrations. Discovered by the runner and the controller as IAdminJob.
        services.AddInterceptedScoped<IAdminJob, RankMemberIdsBackfillJob>();
        return services;
    }
}
