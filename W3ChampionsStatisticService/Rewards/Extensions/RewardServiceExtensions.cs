using Microsoft.Extensions.DependencyInjection;
using W3C.Domain.Rewards.Abstractions;
using W3C.Domain.Repositories;
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
        // Core repositories - using Scoped to avoid multiple instantiations
        services.AddInterceptedScoped<IRewardRepository, RewardRepository>();
        services.AddInterceptedScoped<IRewardAssignmentRepository, RewardAssignmentRepository>();
        services.AddInterceptedScoped<IProductMappingRepository, ProductMappingRepository>();
        services.AddInterceptedScoped<IProductMappingUserAssociationRepository, ProductMappingUserAssociationRepository>();
        services.AddInterceptedScoped<IPatreonAccountLinkRepository, PatreonAccountLinkRepository>();

        // Register repositories that require indexes for startup initialization
        services.AddInterceptedScoped<IRequiresIndexes, RewardAssignmentRepository>();
        services.AddInterceptedScoped<IRequiresIndexes, ProductMappingUserAssociationRepository>();
        services.AddInterceptedScoped<IRequiresIndexes, PatreonAccountLinkRepository>();

        // Core services
        services.AddInterceptedTransient<IRewardService, RewardService>();
        services.AddInterceptedTransient<IProductMappingService, ProductMappingService>();
        services.AddInterceptedTransient<PatreonOAuthService>();

        // Reward providers
        services.AddInterceptedTransient<PatreonProvider>();
        services.AddInterceptedTransient<KoFiProvider>();
        services.AddInterceptedTransient<IRewardProvider, PatreonProvider>();
        services.AddInterceptedTransient<IRewardProvider, KoFiProvider>();

        // Reward modules
        services.AddInterceptedTransient<IRewardModule, PortraitRewardModule>();
        services.AddInterceptedTransient<IRewardModule, ChatColorRewardModule>();
        services.AddInterceptedTransient<IRewardModule, ChatIconRewardModule>();

        // Drift detection services
        services.AddHttpClient<PatreonApiClient>();
        services.AddInterceptedTransient<PatreonDriftDetectionService>();

        // Product mapping reconciliation service
        services.AddInterceptedTransient<IProductMappingReconciliationService, ProductMappingReconciliationService>();
        services.AddInterceptedTransient<ProductMappingReconciliationService>();

        // Background services
        services.AddHostedService<RewardDriftDetectionBackgroundService>();

        return services;
    }
}
