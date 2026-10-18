using System;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using W3C.Domain;
using W3ChampionsStatisticService.Extensions;

namespace WC3ChampionsStatisticService.Tests.Extensions;

[TestFixture]
public class HttpClientServiceCollectionExtensionsTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddW3CHttpClients();
        // A client registered elsewhere (e.g. a typed client) must still inherit the defaults.
        services.AddHttpClient("registered-later");
        return services.BuildServiceProvider();
    }

    [TestCase(HttpClientNames.Matchmaking, 30)]
    [TestCase(HttpClientNames.MatchmakingLogs, 60)]
    [TestCase(HttpClientNames.Identification, 5)]
    [TestCase(HttpClientNames.Chat, 10)]
    [TestCase(HttpClientNames.Update, 15)]
    [TestCase(HttpClientNames.UpdateUpload, 15 * 60)]
    [TestCase(HttpClientNames.UpdateMapMetadataRecompute, 5 * 60)]
    [TestCase(HttpClientNames.Replay, 30)]
    [TestCase(HttpClientNames.War3Info, 15)]
    public void NamedClient_HasExplicitTimeout(string name, int expectedSeconds)
    {
        using var provider = BuildProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(name);

        Assert.That(client.Timeout, Is.EqualTo(TimeSpan.FromSeconds(expectedSeconds)));
    }

    [TestCase("")]
    [TestCase("registered-later")]
    public void UnnamedAndOtherClients_GetDefaultTimeoutInsteadOf100Seconds(string name)
    {
        using var provider = BuildProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(name);

        Assert.That(client.Timeout, Is.EqualTo(HttpClientServiceCollectionExtensions.DefaultTimeout));
        Assert.That(client.Timeout, Is.LessThan(TimeSpan.FromSeconds(100)));
    }

    [TestCase("")]
    [TestCase(HttpClientNames.Matchmaking)]
    [TestCase(HttpClientNames.Identification)]
    [TestCase("registered-later")]
    public void EveryClient_UsesSocketsHandlerWithConnectTimeoutAndBoundedConnectionLifetime(string name)
    {
        using var provider = BuildProvider();
        HttpMessageHandler handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(name);
        while (handler is DelegatingHandler delegating)
        {
            handler = delegating.InnerHandler;
        }

        Assert.That(handler, Is.InstanceOf<SocketsHttpHandler>());
        var sockets = (SocketsHttpHandler)handler;
        Assert.That(sockets.ConnectTimeout, Is.EqualTo(HttpClientServiceCollectionExtensions.ConnectTimeout));
        Assert.That(sockets.ConnectTimeout, Is.LessThanOrEqualTo(TimeSpan.FromSeconds(5)));
        Assert.That(sockets.PooledConnectionLifetime, Is.EqualTo(HttpClientServiceCollectionExtensions.PooledConnectionLifetime));
    }
}
