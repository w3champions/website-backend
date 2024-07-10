using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace W3ChampionsStatisticService.ReadModelBase;

public static class ReadModelExtensions
{
    public static IServiceCollection AddReadModelService<T>(this IServiceCollection services) where T : class, IReadModelHandler
    {
        services.AddTransient<T>();
        services.AddTransient<ReadModelHandler<T>>();
        services.AddSingleton<IHostedService, AsyncServiceBase<ReadModelHandler<T>>>();
        return services;
    }

    public static IServiceCollection AddUnversionedReadModelService<T>(this IServiceCollection services) where T : class, IAsyncUpdatable
    {
        services.AddTransient<T>();
        services.AddSingleton<IHostedService, AsyncServiceBase<T>>();
        return services;
    }
}