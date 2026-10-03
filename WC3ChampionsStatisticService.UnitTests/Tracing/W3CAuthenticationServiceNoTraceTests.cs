using System.Collections.Generic;
using System.Diagnostics;
using Castle.DynamicProxy;
using NUnit.Framework;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Services.Interceptors;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace WC3ChampionsStatisticService.Tests.Tracing;

/// <summary>
/// <c>IW3CAuthenticationService</c> is registered as an interface proxy
/// (<c>AddInterceptedTransient&lt;IW3CAuthenticationService, W3CAuthenticationService&gt;</c>), so
/// <see cref="TracingInterceptor"/> reads <see cref="NoTraceAttribute"/> off the INTERFACE parameters. The
/// service carries no <c>[Trace]</c> today; this guards the day it does, when the raw bearer JWT would
/// otherwise be recorded as the <c>param.jwt</c> activity tag.
/// </summary>
[TestFixture]
public class W3CAuthenticationServiceNoTraceTests
{
    [Test]
    public void GetUserByToken_ThroughTheTracingProxy_NeverTagsTheJwt()
    {
        using var activitySource = new ActivitySource(nameof(W3CAuthenticationServiceNoTraceTests));
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => ReferenceEquals(source, activitySource),
            Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);
        var proxy = new ProxyGenerator().CreateInterfaceProxyWithTarget<IW3CAuthenticationService>(
            new TracedAuthenticationService(), new TracingInterceptor(activitySource));

        proxy.GetUserByToken("header.payload.signature", false);

        Assert.That(stopped, Has.Count.EqualTo(1), "the traced stand-in must produce a span, or this test proves nothing");
        Assert.That(stopped[0].GetTagItem("param.validateLifetime"), Is.EqualTo("False"));
        Assert.That(stopped[0].GetTagItem("param.jwt"), Is.Null, "the bearer JWT must never become a trace tag");
    }

    /// <summary>Stands in for <c>W3CAuthenticationService</c> gaining a class-level <c>[Trace]</c>.</summary>
    [Trace]
    public class TracedAuthenticationService : IW3CAuthenticationService
    {
        public W3CUserAuthenticationDto GetUserByToken(string jwt, bool validateLifetime) =>
            new() { BattleTag = "peter#123" };
    }
}
