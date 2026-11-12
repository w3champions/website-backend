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

    // Any site may read the API, but never with the visitor's credentials: authentication is a bearer header or a
    // hub ticket, never a cookie, and a reflected origin with credentials would let any site send cookies here.
    [Test]
    public void AnyOriginMayReadWithoutCredentials()
    {
        var policy = Build();

        Assert.That(policy.AllowAnyHeader, Is.True);
        Assert.That(policy.AllowAnyMethod, Is.True);
        Assert.That(policy.AllowAnyOrigin, Is.True);
        Assert.That(policy.SupportsCredentials, Is.False);
    }

    // A browser hides every response header that is not safelisted from cross-origin script. The website (another
    // origin) reads two of the network diagnostics API and the Retry-After of the smurf moderation proxy.
    [Test]
    public void ReadableResponseHeaders_AreExposedToCrossOriginScript()
    {
        CollectionAssert.AreEquivalent(new[] { "X-NetDiag-Truncated", "X-Next-Cursor", "Retry-After" }, Build().ExposedHeaders);
    }
}
