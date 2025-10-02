using System;
using Microsoft.Extensions.DependencyInjection;
using Castle.DynamicProxy;
using W3ChampionsStatisticService.Services.Interceptors;
using System.Linq;
using System.Reflection;

namespace W3ChampionsStatisticService.Extensions;

public static class ServiceCollectionExtensions
{
    private static readonly ProxyGenerator ProxyGenerator = new ProxyGenerator();

    // Return the public constructor with the most parameters (greediest)
    private static ConstructorInfo GetGreediestConstructor(Type type) =>
        type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();

    // Resolve constructor parameters safely: prefer DI, then defaults/nulls, then default(T) for value types
    private static object[] ResolveConstructorArgs(IServiceProvider serviceProvider, ParameterInfo[] parameters)
    {
        return parameters.Select(p =>
        {
            var svc = serviceProvider.GetService(p.ParameterType);
            if (svc != null) return svc;
            if (p.HasDefaultValue) return p.DefaultValue;
            if (Nullable.GetUnderlyingType(p.ParameterType) != null) return null;
            if (!p.ParameterType.IsValueType) return null;
            return Activator.CreateInstance(p.ParameterType);
        }).ToArray();
    }

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
            var constructor = GetGreediestConstructor(typeof(TImplementation));
            if (constructor == null)
            {
                throw new InvalidOperationException($"Could not find a public constructor for {typeof(TImplementation)}.");
            }
            var constructorArgs = ResolveConstructorArgs(serviceProvider, constructor.GetParameters());

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
            var constructor = GetGreediestConstructor(typeof(TImplementation));
            if (constructor == null)
            {
                throw new InvalidOperationException($"Could not find a public constructor for {typeof(TImplementation)}.");
            }
            var constructorArgs = ResolveConstructorArgs(serviceProvider, constructor.GetParameters());

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
            var constructor = GetGreediestConstructor(typeof(TImplementation));
            if (constructor == null)
            {
                throw new InvalidOperationException($"Could not find a public constructor for {typeof(TImplementation)}.");
            }
            var constructorArgs = ResolveConstructorArgs(serviceProvider, constructor.GetParameters());

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
            var constructor = GetGreediestConstructor(typeof(TImplementation));
            if (constructor == null)
            {
                throw new InvalidOperationException($"Could not find a public constructor for {typeof(TImplementation)}.");
            }
            var constructorArgs = ResolveConstructorArgs(serviceProvider, constructor.GetParameters());

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
            var constructor = GetGreediestConstructor(typeof(TImplementation));
            if (constructor == null)
            {
                throw new InvalidOperationException($"Could not find a public constructor for {typeof(TImplementation)}.");
            }
            var constructorArgs = ResolveConstructorArgs(serviceProvider, constructor.GetParameters());

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
            var constructor = GetGreediestConstructor(typeof(TImplementation));
            if (constructor == null)
            {
                throw new InvalidOperationException($"Could not find a public constructor for {typeof(TImplementation)}.");
            }
            var constructorArgs = ResolveConstructorArgs(serviceProvider, constructor.GetParameters());

            return ProxyGenerator.CreateClassProxy<TImplementation>(constructorArgs, interceptor);
        });
        return services;
    }
}
