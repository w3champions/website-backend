using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
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

    private static ResourceExecutingContext CreateResourceContext(
        HttpContext httpContext, IList<IValueProviderFactory> valueProviderFactories)
        => new(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(),
            valueProviderFactories);

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
