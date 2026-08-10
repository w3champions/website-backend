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

    /// <summary>
    /// Wraps the LAST existing registration of <typeparamref name="TInterface"/> in
    /// <typeparamref name="TDecorator"/>, in place — same position in the collection, same lifetime.
    /// <para>
    /// This exists because <see cref="AddInterceptedTransient{TInterface,TImplementation}"/> registers
    /// the interface as a Castle DynamicProxy over the concrete type. A decorator cannot be layered by
    /// re-registering the interface: that helper resolves constructor arguments straight from the
    /// container by type, so a decorator taking <typeparamref name="TInterface"/> would resolve to
    /// ITSELF and recurse. Building the inner instance from the CAPTURED descriptor is what keeps the
    /// tracing proxy alive underneath the decorator.
    /// </para>
    /// <para>
    /// Must be called AFTER the registration it wraps. Decorating twice nests in call order:
    /// the second decorator wraps the first.
    /// </para>
    /// </summary>
    public static IServiceCollection Decorate<TInterface, TDecorator>(this IServiceCollection services)
        where TInterface : class
        where TDecorator : class, TInterface
    {
        var index = -1;
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(TInterface))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            throw new InvalidOperationException(
                $"Cannot decorate {typeof(TInterface).Name} with {typeof(TDecorator).Name}: it has no existing "
                + "registration. Decorate must be called AFTER the registration it wraps.");
        }

        var inner = services[index];

        services[index] = new ServiceDescriptor(
            typeof(TInterface),
            serviceProvider => ActivatorUtilities.CreateInstance<TDecorator>(
                serviceProvider,
                CreateInner(serviceProvider, inner)),
            inner.Lifetime);

        return services;
    }

    // Rebuilds the captured registration by whichever of the three ServiceDescriptor forms it used.
    private static object CreateInner(IServiceProvider serviceProvider, ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance != null) return descriptor.ImplementationInstance;
        if (descriptor.ImplementationFactory != null) return descriptor.ImplementationFactory(serviceProvider);
        return ActivatorUtilities.CreateInstance(serviceProvider, descriptor.ImplementationType);
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
