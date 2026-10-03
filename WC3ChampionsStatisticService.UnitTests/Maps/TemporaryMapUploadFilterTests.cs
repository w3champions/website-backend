using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using NUnit.Framework;
using W3ChampionsStatisticService.Maps;

namespace WC3ChampionsStatisticService.Tests.Maps;

[TestFixture]
public class TemporaryMapUploadFilterTests
{
    [Test]
    public void BodyLimitFilter_RaisesTheLimitToTheTransportCeiling()
    {
        var httpContext = new DefaultHttpContext();
        var feature = new FakeMaxBodySizeFeature();
        httpContext.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);

        new TemporaryMapUploadBodyLimitAttribute().OnResourceExecuting(
            CreateResourceContext(httpContext, new List<IValueProviderFactory>()));

        Assert.That(feature.MaxRequestBodySize, Is.EqualTo(TemporaryMapLimits.TransportBodyBytes));
        Assert.That(TemporaryMapLimits.TransportBodyBytes, Is.EqualTo(269_484_032));
        Assert.That(TemporaryMapLimits.TransportBodyBytes - TemporaryMapLimits.MaxFileBytes, Is.EqualTo(1024 * 1024));
    }

    [Test]
    public void BodyLimitFilter_LeavesAReadOnlyLimitUntouched()
    {
        // Kestrel makes the feature read-only once the body has started to be read; its setter then throws.
        var httpContext = new DefaultHttpContext();
        var feature = new FakeMaxBodySizeFeature { IsReadOnly = true };
        httpContext.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);

        Assert.DoesNotThrow(() => new TemporaryMapUploadBodyLimitAttribute().OnResourceExecuting(
            CreateResourceContext(httpContext, new List<IValueProviderFactory>())));

        Assert.That(feature.MaxRequestBodySize, Is.EqualTo(FakeMaxBodySizeFeature.KestrelGlobalLimit));
    }

    [Test]
    public void BodyLimitFilter_ToleratesAServerWithoutTheFeature()
    {
        var httpContext = new DefaultHttpContext();

        Assert.DoesNotThrow(() => new TemporaryMapUploadBodyLimitAttribute().OnResourceExecuting(
            CreateResourceContext(httpContext, new List<IValueProviderFactory>())));
    }

    [Test]
    public void BodyLimitFilter_SetsTheMinimumBodyDataRate_SoATrickledBodyCannotHoldASlotForDays()
    {
        // Kestrel's default floor (240 B/s after 5 s) lets a 256 MiB body take ~13 days while holding a gate slot; the
        // filter raises it so a full upload must finish within a few hours and a trickle is cut off after the grace
        // period: Kestrel flags the request and cancels the pending read, the read surfaces as its 408
        // BadHttpRequestException (RequestBodyTimeout) with RequestAborted not cancelled, and the controller aborts
        // the connection, answers nothing and releases the slot and the partial spool (TemporaryMapsControllerUploadTests
        // pins that arm).
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<IHttpMaxRequestBodySizeFeature>(new FakeMaxBodySizeFeature());
        var dataRate = new FakeMinDataRateFeature();
        httpContext.Features.Set<IHttpMinRequestBodyDataRateFeature>(dataRate);

        new TemporaryMapUploadBodyLimitAttribute().OnResourceExecuting(
            CreateResourceContext(httpContext, new List<IValueProviderFactory>()));

        Assert.That(dataRate.MinDataRate, Is.Not.Null);
        Assert.That(dataRate.MinDataRate!.BytesPerSecond, Is.EqualTo(TemporaryMapLimits.MinUploadBytesPerSecond));
        Assert.That(dataRate.MinDataRate.GracePeriod, Is.EqualTo(TemporaryMapLimits.MinUploadGracePeriod));
        Assert.That(TemporaryMapLimits.MinUploadBytesPerSecond, Is.EqualTo(32 * 1024));
        Assert.That(TemporaryMapLimits.MinUploadGracePeriod, Is.EqualTo(TimeSpan.FromSeconds(30)));
        // A full-size upload at the floor: bounded to hours, not days.
        Assert.That(TemporaryMapLimits.MaxFileBytes / TemporaryMapLimits.MinUploadBytesPerSecond, Is.LessThan(TimeSpan.FromHours(3).TotalSeconds));
    }

    [Test]
    public void BodyLimitFilter_SetsTheDataRateEvenWhenTheSizeLimitIsReadOnly()
    {
        // The two features are independent: a body already being read keeps the server's size limit, but the data-rate
        // floor still applies to the rest of that body.
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<IHttpMaxRequestBodySizeFeature>(new FakeMaxBodySizeFeature { IsReadOnly = true });
        var dataRate = new FakeMinDataRateFeature();
        httpContext.Features.Set<IHttpMinRequestBodyDataRateFeature>(dataRate);

        new TemporaryMapUploadBodyLimitAttribute().OnResourceExecuting(
            CreateResourceContext(httpContext, new List<IValueProviderFactory>()));

        Assert.That(dataRate.MinDataRate?.BytesPerSecond, Is.EqualTo(TemporaryMapLimits.MinUploadBytesPerSecond));
    }

    [Test]
    public void BodyLimitFilter_ToleratesAServerWithoutTheDataRateFeature()
    {
        // A test host or a server other than Kestrel: the size limit is still raised, nothing throws.
        var httpContext = new DefaultHttpContext();
        var feature = new FakeMaxBodySizeFeature();
        httpContext.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);

        Assert.DoesNotThrow(() => new TemporaryMapUploadBodyLimitAttribute().OnResourceExecuting(
            CreateResourceContext(httpContext, new List<IValueProviderFactory>())));

        Assert.That(feature.MaxRequestBodySize, Is.EqualTo(TemporaryMapLimits.TransportBodyBytes));
    }

    [Test]
    public void DisableFormValueModelBinding_RemovesEveryFormValueProvider()
    {
        var factories = new List<IValueProviderFactory>
        {
            new FormValueProviderFactory(),
            new FormFileValueProviderFactory(),
            new JQueryFormValueProviderFactory(),
            new QueryStringValueProviderFactory(),
        };

        new DisableFormValueModelBindingAttribute().OnResourceExecuting(
            CreateResourceContext(new DefaultHttpContext(), factories));

        Assert.That(factories, Has.Count.EqualTo(1));
        Assert.That(factories[0], Is.InstanceOf<QueryStringValueProviderFactory>());
    }

    /// <summary>The actions that read their multipart body themselves, by name: the reflection below cannot see a body read.</summary>
    private static readonly (Type Controller, string Action)[] KnownStreamingActions =
    [
        (typeof(TemporaryMapsController), nameof(TemporaryMapsController.Upload)),
        (typeof(MapsController), nameof(MapsController.CreateMapFile)),
    ];

    [Test]
    public void EveryStreamingAction_KeepsModelBindingOffItsBody()
    {
        // An action that streams its own multipart body must carry [DisableFormValueModelBinding]: with any bindable
        // parameter (a route id, a battleTag the permission filter fills in) MVC's form value provider otherwise reads
        // and buffers the whole body before the action — and before an action-filter permission check — leaving nothing
        // to forward. That is the regression the admin passthrough carried between ec3a81d and 39a1b49. "Streams its
        // body" is approximated as: carries [TemporaryMapUploadBodyLimit], or is one of the known streaming actions —
        // which must carry it too: the two attributes travel together on every action that streams a map file.
        var actions = typeof(MapsController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>() == null)
            .ToArray();
        var known = KnownStreamingActions
            .Select(k => actions.SingleOrDefault(m => m.DeclaringType == k.Controller && m.Name == k.Action)
                         ?? throw new InvalidOperationException($"{k.Controller.Name}.{k.Action} is no longer an action; update KnownStreamingActions"))
            .ToArray();
        var streaming = actions
            .Where(m => m.GetCustomAttribute<TemporaryMapUploadBodyLimitAttribute>() != null)
            .Union(known)
            .ToArray();

        Assert.That(known.Where(m => m.GetCustomAttribute<TemporaryMapUploadBodyLimitAttribute>() == null)
                .Select(m => $"{m.DeclaringType!.Name}.{m.Name}"),
            Is.Empty, "a known streaming action without [TemporaryMapUploadBodyLimit] runs under the global 128 MiB ceiling");
        Assert.That(streaming.Where(m => m.GetCustomAttribute<DisableFormValueModelBindingAttribute>() == null)
                .Select(m => $"{m.DeclaringType!.Name}.{m.Name}"),
            Is.Empty, "a streaming action without [DisableFormValueModelBinding] has its body read by model binding first");
    }

    private static ResourceExecutingContext CreateResourceContext(
        HttpContext httpContext, IList<IValueProviderFactory> valueProviderFactories)
        => new(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(),
            valueProviderFactories);

    private sealed class FakeMinDataRateFeature : IHttpMinRequestBodyDataRateFeature
    {
        public MinDataRate MinDataRate { get; set; }
    }

    private sealed class FakeMaxBodySizeFeature : IHttpMaxRequestBodySizeFeature
    {
        public const long KestrelGlobalLimit = 0x8000000;

        private long? _maxRequestBodySize = KestrelGlobalLimit;

        public bool IsReadOnly { get; init; }

        public long? MaxRequestBodySize
        {
            get => _maxRequestBodySize;
            set => _maxRequestBodySize = IsReadOnly
                ? throw new InvalidOperationException("The maximum request body size cannot be modified after the app has already started reading the request body.")
                : value;
        }
    }
}
