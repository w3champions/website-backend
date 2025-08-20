using Microsoft.Extensions.DependencyInjection;
using W3C.Domain.Rewards.Abstractions;
using W3C.Domain.Rewards.Repositories;
using W3ChampionsStatisticService.Extensions;
using W3ChampionsStatisticService.Rewards.BackgroundServices;
using W3ChampionsStatisticService.Rewards.Modules;
using W3ChampionsStatisticService.Rewards.Providers.KoFi;
using W3ChampionsStatisticService.Rewards.Providers.Patreon;
using W3ChampionsStatisticService.Rewards.Repositories;
using W3ChampionsStatisticService.Rewards.Services;

namespace W3ChampionsStatisticService.Rewards.Extensions;

public static class RewardServiceExtensions
{
    public static IServiceCollection AddRewardServices(this IServiceCollection services)
    {
        // Core repositories
        services.AddInterceptedTransient<IRewardRepository, RewardRepository>();
        services.AddInterceptedTransient<IRewardAssignmentRepository, RewardAssignmentRepository>();
        services.AddInterceptedTransient<IProviderConfigurationRepository, ProviderConfigurationRepository>();
        services.AddInterceptedTransient<IPatreonAccountLinkRepository, PatreonAccountLinkRepository>();

        // Core services
        services.AddInterceptedTransient<IRewardService, RewardService>();
        services.AddInterceptedTransient<PatreonOAuthService>();

        // Reward providers
        services.AddInterceptedTransient<PatreonProvider>();
        services.AddInterceptedTransient<KoFiProvider>();
        services.AddInterceptedTransient<IRewardProvider, PatreonProvider>();
        services.AddInterceptedTransient<IRewardProvider, KoFiProvider>();

        // Reward modules
        services.AddInterceptedTransient<IRewardModule, PortraitRewardModule>();

        // Drift detection services
        services.AddHttpClient<PatreonApiClient>();
        services.AddInterceptedTransient<PatreonDriftDetectionService>();

        // Background services
        services.AddHostedService<RewardDriftDetectionBackgroundService>();

        return services;
    }
}