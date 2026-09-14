using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Castle.DynamicProxy;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.RateLimiting.Models;
using W3ChampionsStatisticService.RateLimiting.Repositories;
using W3ChampionsStatisticService.RateLimiting.Services;
using W3ChampionsStatisticService.Services.Interceptors;

namespace WC3ChampionsStatisticService.Tests.Tracing;

/// <summary>
/// <c>IApiTokenService</c> and <c>IApiTokenRepository</c> are registered as interface proxies
/// (<c>AddInterceptedSingleton&lt;I, T&gt;</c>), so <see cref="TracingInterceptor"/> reads <see cref="NoTraceAttribute"/>
/// off the INTERFACE parameters and would otherwise record a raw API token as a <c>param.token</c> activity tag. Each test also checks a non-credential tag, so a span without any tags cannot
/// pass vacuously.
/// </summary>
[TestFixture]
public class CredentialParameterNoTraceTests
{
    private const string Credential = "secret-credential-value";

    [Test]
    public async Task ApiTokenService_ValidateToken_ThroughTheTracingProxy_NeverTagsTheToken()
    {
        // The real service, whose ValidateToken carries [Trace] in production.
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new ApiTokenService(Mock.Of<IApiTokenRepository>(), cache, NullLogger<ApiTokenService>.Instance);

        var spans = await CaptureSpans<IApiTokenService>(service, proxy => proxy.ValidateToken(Credential, "192.0.2.1", "scope"));

        Assert.That(spans, Has.Count.EqualTo(1));
        Assert.That(spans[0].DisplayName, Is.EqualTo("ApiTokenService.ValidateToken"));
        Assert.That(spans[0].GetTagItem("param.ipAddress"), Is.EqualTo("192.0.2.1"));
        Assert.That(spans[0].GetTagItem("param.token"), Is.Null, "a raw API token must never become a trace tag");
    }

    [Test]
    public async Task ApiTokenService_GetRateLimitsForScope_ThroughTheTracingProxy_NeverTagsTheToken()
    {
        var spans = await CaptureSpans<IApiTokenService>(
            new TracedApiTokenService(), proxy => proxy.GetRateLimitsForScope(Credential, "scope"));

        Assert.That(spans, Has.Count.EqualTo(1));
        Assert.That(spans[0].GetTagItem("param.scope"), Is.EqualTo("scope"));
        Assert.That(spans[0].GetTagItem("param.token"), Is.Null, "a raw API token must never become a trace tag");
    }

    [Test]
    public async Task ApiTokenRepository_TokenLookups_ThroughTheTracingProxy_NeverTagTheToken()
    {
        var spans = await CaptureSpans<IApiTokenRepository>(new TracedApiTokenRepository(), async proxy =>
        {
            await proxy.GetById("token-id");
            await proxy.GetByToken(Credential);
            await proxy.UpdateLastUsed(Credential);
        });

        Assert.That(spans, Has.Count.EqualTo(3));
        Assert.That(spans[0].GetTagItem("param.id"), Is.EqualTo("token-id"));
        Assert.That(spans[1].DisplayName, Does.EndWith(".GetByToken"));
        Assert.That(spans[1].GetTagItem("param.token"), Is.Null, "a raw API token must never become a trace tag");
        Assert.That(spans[2].DisplayName, Does.EndWith(".UpdateLastUsed"));
        Assert.That(spans[2].GetTagItem("param.token"), Is.Null, "a raw API token must never become a trace tag");
    }

    /// <summary>Invokes <paramref name="act"/> on an interface proxy of <paramref name="target"/> and returns the stopped spans in order.</summary>
    private static async Task<List<Activity>> CaptureSpans<TInterface>(TInterface target, Func<TInterface, Task> act)
        where TInterface : class
    {
        using var activitySource = new ActivitySource(nameof(CredentialParameterNoTraceTests));
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => ReferenceEquals(source, activitySource),
            Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);
        var proxy = new ProxyGenerator().CreateInterfaceProxyWithTarget(target, new TracingInterceptor(activitySource));

        await act(proxy);

        return stopped;
    }

    /// <summary>Stands in for <c>ApiTokenService</c> with <c>GetRateLimitsForScope</c> traced (it is not today).</summary>
    [Trace]
    public class TracedApiTokenService : IApiTokenService
    {
        public Task<ApiToken> ValidateToken(string token, string ipAddress, string scope = null) => Task.FromResult<ApiToken>(null);
        public Task<(int hourlyLimit, int dailyLimit)?> GetRateLimitsForScope(string token, string scope) =>
            Task.FromResult<(int hourlyLimit, int dailyLimit)?>(null);
    }

    /// <summary>
    /// Stands in for the Mongo-backed <c>ApiTokenRepository</c> (whose lookups carry [Trace]) without a database;
    /// <c>UpdateLastUsed</c> is traced here too, which it is not today.
    /// </summary>
    [Trace]
    public class TracedApiTokenRepository : IApiTokenRepository
    {
        public Task<ApiToken> GetByToken(string token) => Task.FromResult<ApiToken>(null);
        public Task<ApiToken> GetById(string id) => Task.FromResult<ApiToken>(null);
        public Task<List<ApiToken>> GetAll() => Task.FromResult(new List<ApiToken>());
        public Task Create(ApiToken apiToken) => Task.CompletedTask;
        public Task Update(ApiToken apiToken) => Task.CompletedTask;
        public Task Delete(string id) => Task.CompletedTask;
        public Task UpdateLastUsed(string token) => Task.CompletedTask;
    }
}
