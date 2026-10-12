using System;
using Microsoft.AspNetCore.Cors.Infrastructure;
using NUnit.Framework;
using W3ChampionsStatisticService;

namespace WC3ChampionsStatisticService.Tests;

[TestFixture]
public class CorsPolicyConfigurationTests
{
    private static CorsPolicy Build()
    {
        var builder = new CorsPolicyBuilder();
        CorsPolicyConfiguration.Configure(builder);
        return builder.Build();
    }

    [Test]
    public void Preflights_AreCacheableByBrowsers()
    {
        // Without Access-Control-Max-Age browsers cache a preflight for ~5 s, so every reconnect
        // burst doubles the request count with OPTIONS calls.
        Assert.That(Build().PreflightMaxAge, Is.EqualTo(TimeSpan.FromHours(2)));
    }

    [Test]
    public void ExistingPolicyIsUnchanged()
    {
        var policy = Build();

        Assert.That(policy.AllowAnyHeader, Is.True);
        Assert.That(policy.AllowAnyMethod, Is.True);
        Assert.That(policy.SupportsCredentials, Is.True);
        Assert.That(policy.IsOriginAllowed("https://w3champions.com"), Is.True);
        Assert.That(policy.IsOriginAllowed("tauri://localhost"), Is.True);
    }

    // A browser hides every response header that is not safelisted from cross-origin script. The website (another
    // origin) reads these two of the network diagnostics API.
    [Test]
    public void NetworkDiagnosticsResponseHeaders_AreReadableByCrossOriginScript()
    {
        CollectionAssert.AreEquivalent(new[] { "X-NetDiag-Truncated", "X-Next-Cursor" }, Build().ExposedHeaders);
    }
}
