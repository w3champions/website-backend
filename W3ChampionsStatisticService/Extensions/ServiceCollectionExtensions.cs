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
        // Register the concrete implementation with a factory that constructs it using
        // safe constructor-arg resolution to avoid asking the container for non-service
        // parameters (like TimeSpan?). Then register the interface as a proxy that
        // delegates to that implementation.
        services.AddSingleton<TImplementation>(serviceProvider =>
        {
            var constructor = typeof(TImplementation).GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
            if (constructor == null)
            {
                throw new InvalidOperationException($"Could not find a public constructor for {typeof(TImplementation)}.");
            }
            var constructorArgs = constructor.GetParameters()
                .Select(p =>
                {
                    var svc = serviceProvider.GetService(p.ParameterType);
                    if (svc != null) return svc;
                    if (p.HasDefaultValue) return p.DefaultValue;
                    if (Nullable.GetUnderlyingType(p.ParameterType) != null) return null;
                    if (!p.ParameterType.IsValueType) return null;
                    return Activator.CreateInstance(p.ParameterType);
                })
                .ToArray();

            return (TImplementation)Activator.CreateInstance(typeof(TImplementation), constructorArgs);
        });

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
        // Register transient implementation using a factory that constructs the
        // instance with safe constructor resolution, then register the interface
        // as a proxy that delegates to that implementation.
        services.AddTransient<TImplementation>(serviceProvider =>
        {
            var constructor = typeof(TImplementation).GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
            if (constructor == null)
            {
                throw new InvalidOperationException($"Could not find a public constructor for {typeof(TImplementation)}.");
            }
            var constructorArgs = constructor.GetParameters()
                .Select(p =>
                {
                    var svc = serviceProvider.GetService(p.ParameterType);
                    if (svc != null) return svc;
                    if (p.HasDefaultValue) return p.DefaultValue;
                    if (Nullable.GetUnderlyingType(p.ParameterType) != null) return null;
                    if (!p.ParameterType.IsValueType) return null;
                    return Activator.CreateInstance(p.ParameterType);
                })
                .ToArray();

            return (TImplementation)Activator.CreateInstance(typeof(TImplementation), constructorArgs);
        });

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
        // Register scoped implementation using a factory that constructs the
        // instance with safe constructor resolution, then register the interface
        // as a proxy that delegates to that implementation.
        services.AddScoped<TImplementation>(serviceProvider =>
        {
            var constructor = typeof(TImplementation).GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
            if (constructor == null)
            {
                throw new InvalidOperationException($"Could not find a public constructor for {typeof(TImplementation)}.");
            }
            var constructorArgs = constructor.GetParameters()
                .Select(p =>
                {
                    var svc = serviceProvider.GetService(p.ParameterType);
                    if (svc != null) return svc;
                    if (p.HasDefaultValue) return p.DefaultValue;
                    if (Nullable.GetUnderlyingType(p.ParameterType) != null) return null;
                    if (!p.ParameterType.IsValueType) return null;
                    return Activator.CreateInstance(p.ParameterType);
                })
                .ToArray();

            return (TImplementation)Activator.CreateInstance(typeof(TImplementation), constructorArgs);
        });

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
                .Select(p =>
                {
                    // Try to resolve from DI first
                    var svc = serviceProvider.GetService(p.ParameterType);
                    if (svc != null) return svc;

                    // If parameter has a default value, use it
                    if (p.HasDefaultValue) return p.DefaultValue;

                    // If nullable value type (e.g., TimeSpan?), return null
                    if (Nullable.GetUnderlyingType(p.ParameterType) != null) return null;

                    // For reference types not registered, return null
                    if (!p.ParameterType.IsValueType) return null;

                    // For value types, use default(T)
                    return Activator.CreateInstance(p.ParameterType);
                })
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
                .Select(p =>
                {
                    var svc = serviceProvider.GetService(p.ParameterType);
                    if (svc != null) return svc;
                    if (p.HasDefaultValue) return p.DefaultValue;
                    if (Nullable.GetUnderlyingType(p.ParameterType) != null) return null;
                    if (!p.ParameterType.IsValueType) return null;
                    return Activator.CreateInstance(p.ParameterType);
                })
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
                .Select(p =>
                {
                    var svc = serviceProvider.GetService(p.ParameterType);
                    if (svc != null) return svc;
                    if (p.HasDefaultValue) return p.DefaultValue;
                    if (Nullable.GetUnderlyingType(p.ParameterType) != null) return null;
                    if (!p.ParameterType.IsValueType) return null;
                    return Activator.CreateInstance(p.ParameterType);
                })
                .ToArray();

            return ProxyGenerator.CreateClassProxy<TImplementation>(constructorArgs, interceptor);
        });
        return services;
    }
}
