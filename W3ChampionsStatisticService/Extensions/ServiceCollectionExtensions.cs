using System;
using Microsoft.Extensions.DependencyInjection;
using Castle.DynamicProxy;
using W3ChampionsStatisticService.Services.Interceptors;
using System.Linq;

namespace W3ChampionsStatisticService.Extensions;

public static class ServiceCollectionExtensions
{
    private static readonly ProxyGenerator ProxyGenerator = new ProxyGenerator();

    public static IServiceCollection AddInterceptedSingleton<TInterface, TImplementation>(
        this IServiceCollection services)
        where TInterface : class
        where TImplementation : class, TInterface
    {
        services.AddSingleton<TImplementation>(); // Register the actual implementation
        services.AddSingleton<TInterface>(serviceProvider =>
        {
            var implementation = serviceProvider.GetRequiredService<TImplementation>();
            var interceptor = serviceProvider.GetRequiredService<TracingInterceptor>();
            return ProxyGenerator.CreateInterfaceProxyWithTarget<TInterface>(implementation, interceptor);
        });
        return services;
    }

    public static IServiceCollection AddInterceptedTransient<TInterface, TImplementation>(
        this IServiceCollection services)
        where TInterface : class
        where TImplementation : class, TInterface
    {
        services.AddTransient<TImplementation>(); // Register the actual implementation
        services.AddTransient<TInterface>(serviceProvider =>
        {
            var implementation = serviceProvider.GetRequiredService<TImplementation>();
            var interceptor = serviceProvider.GetRequiredService<TracingInterceptor>();
            return ProxyGenerator.CreateInterfaceProxyWithTarget<TInterface>(implementation, interceptor);
        });
        return services;
    }

    public static IServiceCollection AddInterceptedScoped<TInterface, TImplementation>(
        this IServiceCollection services)
        where TInterface : class
        where TImplementation : class, TInterface
    {
        services.AddScoped<TImplementation>(); // Register the actual implementation
        services.AddScoped<TInterface>(serviceProvider =>
        {
            var implementation = serviceProvider.GetRequiredService<TImplementation>();
            var interceptor = serviceProvider.GetRequiredService<TracingInterceptor>();
            return ProxyGenerator.CreateInterfaceProxyWithTarget<TInterface>(implementation, interceptor);
        });
        return services;
    }

    // AddInterceptedSingleton for a concrete type (TImplementation is the service type)
    public static IServiceCollection AddInterceptedSingleton<TImplementation>(
        this IServiceCollection services)
        where TImplementation : class
    {
        services.AddSingleton<TImplementation>(serviceProvider =>
        {
            var interceptor = serviceProvider.GetRequiredService<TracingInterceptor>();
            // Get constructor with most parameters (assuming it's the one DI would use)
            var constructor = typeof(TImplementation).GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
            if (constructor == null)
            {
                throw new InvalidOperationException($"Could not find a public constructor for {typeof(TImplementation)}.");
            }
            var constructorArgs = constructor.GetParameters()
                .Select(p => serviceProvider.GetRequiredService(p.ParameterType))
                .ToArray();

            return ProxyGenerator.CreateClassProxy<TImplementation>(constructorArgs, interceptor);
        });
        return services;
    }

    // AddInterceptedTransient for a concrete type (TImplementation is the service type)
    public static IServiceCollection AddInterceptedTransient<TImplementation>(
        this IServiceCollection services)
        where TImplementation : class
    {
        services.AddTransient<TImplementation>(serviceProvider =>
        {
            var interceptor = serviceProvider.GetRequiredService<TracingInterceptor>();
            var constructor = typeof(TImplementation).GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
            if (constructor == null)
            {
                throw new InvalidOperationException($"Could not find a public constructor for {typeof(TImplementation)}.");
            }
            var constructorArgs = constructor.GetParameters()
                .Select(p => serviceProvider.GetRequiredService(p.ParameterType))
                .ToArray();

            return ProxyGenerator.CreateClassProxy<TImplementation>(constructorArgs, interceptor);
        });
        return services;
    }

    // AddInterceptedScoped for a concrete type (TImplementation is the service type)
    public static IServiceCollection AddInterceptedScoped<TImplementation>(
        this IServiceCollection services)
        where TImplementation : class
    {
        services.AddScoped<TImplementation>(serviceProvider =>
        {
            var interceptor = serviceProvider.GetRequiredService<TracingInterceptor>();
            var constructor = typeof(TImplementation).GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
            if (constructor == null)
            {
                throw new InvalidOperationException($"Could not find a public constructor for {typeof(TImplementation)}.");
            }
            var constructorArgs = constructor.GetParameters()
                .Select(p => serviceProvider.GetRequiredService(p.ParameterType))
                .ToArray();

            return ProxyGenerator.CreateClassProxy<TImplementation>(constructorArgs, interceptor);
        });
        return services;
    }
}
