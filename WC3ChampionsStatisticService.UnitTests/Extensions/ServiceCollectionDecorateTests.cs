using System;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using W3ChampionsStatisticService.Extensions;

namespace WC3ChampionsStatisticService.Tests.Extensions;

[TestFixture]
public class ServiceCollectionDecorateTests
{
    private interface IGreeter
    {
        string Greet();
    }

    private class Inner : IGreeter
    {
        public string Greet() => "inner";
    }

    private class Outer(IGreeter inner) : IGreeter
    {
        public string Greet() => $"outer({inner.Greet()})";
    }

    private class Second(IGreeter inner) : IGreeter
    {
        public string Greet() => $"second({inner.Greet()})";
    }

    [Test]
    public void Decorate_WrapsTheExistingRegistration()
    {
        var services = new ServiceCollection();
        services.AddTransient<IGreeter, Inner>();

        services.Decorate<IGreeter, Outer>();

        var resolved = services.BuildServiceProvider().GetRequiredService<IGreeter>();
        Assert.That(resolved.Greet(), Is.EqualTo("outer(inner)"));
    }

    [Test]
    public void Decorate_PreservesAFactoryRegistration()
    {
        // AddInterceptedTransient registers the interface via an ImplementationFactory that builds a
        // Castle proxy. If Decorate ignored the factory and reflected over ImplementationType (null
        // here), the tracing proxy would be silently dropped.
        var services = new ServiceCollection();
        services.AddTransient<IGreeter>(_ => new Inner());

        services.Decorate<IGreeter, Outer>();

        var resolved = services.BuildServiceProvider().GetRequiredService<IGreeter>();
        Assert.That(resolved.Greet(), Is.EqualTo("outer(inner)"));
    }

    [Test]
    public void Decorate_PreservesLifetime()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IGreeter, Inner>();

        services.Decorate<IGreeter, Outer>();

        var provider = services.BuildServiceProvider();
        Assert.That(provider.GetRequiredService<IGreeter>(), Is.SameAs(provider.GetRequiredService<IGreeter>()));
    }

    [Test]
    public void Decorate_Twice_NestsInRegistrationOrder()
    {
        var services = new ServiceCollection();
        services.AddTransient<IGreeter, Inner>();

        services.Decorate<IGreeter, Outer>();
        services.Decorate<IGreeter, Second>();

        var resolved = services.BuildServiceProvider().GetRequiredService<IGreeter>();
        Assert.That(resolved.Greet(), Is.EqualTo("second(outer(inner))"));
    }

    [Test]
    public void Decorate_WithNoExistingRegistration_ThrowsWithAnActionableMessage()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() => services.Decorate<IGreeter, Outer>());
        Assert.That(ex.Message, Does.Contain("IGreeter"));
        Assert.That(ex.Message, Does.Contain("AFTER"));
    }
}
