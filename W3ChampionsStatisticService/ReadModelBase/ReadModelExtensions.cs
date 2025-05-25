using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace W3ChampionsStatisticService.ReadModelBase;

public static class ReadModelExtensions
{
    public static IServiceCollection AddMatchFinishedReadModelService<T>(this IServiceCollection services) where T : class, IMatchFinishedReadModelHandler
    {
        services.AddTransient<T>();
        services.AddTransient<MatchFinishedReadModelHandler<T>>();
        services.AddSingleton<IHostedService, AsyncServiceBase<MatchFinishedReadModelHandler<T>>>();
        return services;
    }

    public static IServiceCollection AddUnversionedReadModelService<T>(this IServiceCollection services) where T : class, IAsyncUpdatable
    {
        services.AddTransient<T>();
        services.AddSingleton<IHostedService, AsyncServiceBase<T>>();
        return services;
    }
}
