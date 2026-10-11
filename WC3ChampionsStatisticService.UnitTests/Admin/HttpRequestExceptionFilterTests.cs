using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using NUnit.Framework;
using W3ChampionsStatisticService.WebApi.ExceptionFilters;

namespace WC3ChampionsStatisticService.Tests.Admin;

[TestFixture]
public class HttpRequestExceptionFilterTests
{
    private const string Host = "matchmaking-service-local:25048";

    [Test]
    public void UnreachableUpstreamAnswersGeneric502WithoutHostDetails()
    {
        var inner = new SocketException((int)SocketError.ConnectionRefused);
        var ex = new HttpRequestException($"Connection refused ({Host})", inner);

        var (status, error) = Run(ex);

        Assert.That(status, Is.EqualTo(502));
        Assert.That(error, Is.EqualTo(HttpRequestExceptionFilter.UnreachableMessage));
        Assert.That(error, Does.Not.Contain("25048"));
    }

    [Test]
    public void TimedOutHttpClientCallAnswersGeneric504()
    {
        var ex = new TaskCanceledException($"The request was canceled due to the configured HttpClient.Timeout ({Host})",
            new TimeoutException("A task was canceled."));

        var (status, error) = Run(ex);

        Assert.That(status, Is.EqualTo(504));
        Assert.That(error, Is.EqualTo(HttpRequestExceptionFilter.TimeoutMessage));
    }

    [Test]
    public void HttpRequestExceptionWrappingATimeoutAnswers504()
    {
        var (status, _) = Run(new HttpRequestException($"timeout {Host}", new TimeoutException()));

        Assert.That(status, Is.EqualTo(504));
    }

    [Test]
    public void ClientAbortIsLeftAlone()
    {
        var context = RunFilter(new TaskCanceledException("client went away"));

        Assert.That(context.ExceptionHandled, Is.False);
        Assert.That(context.Result, Is.Null);
    }

    [TestCase(HttpStatusCode.Found)]
    [TestCase(HttpStatusCode.Accepted)]
    public void NonErrorStatusIsNotPassedThroughAndAnswers502(HttpStatusCode upstream)
    {
        var (status, error) = Run(new HttpRequestException($"odd {Host}", null, upstream));

        Assert.That(status, Is.EqualTo(502));
        Assert.That(error, Is.EqualTo(HttpRequestExceptionFilter.UpstreamErrorMessage));
    }

    [TestCase(HttpStatusCode.InternalServerError)]
    [TestCase(HttpStatusCode.ServiceUnavailable)]
    public void UpstreamServerErrorKeepsStatusButHidesBody(HttpStatusCode upstream)
    {
        var (status, error) = Run(new HttpRequestException($"connect ECONNREFUSED {Host}", null, upstream));

        Assert.That(status, Is.EqualTo((int)upstream));
        Assert.That(error, Is.EqualTo(HttpRequestExceptionFilter.UpstreamErrorMessage));
    }

    [TestCase(HttpStatusCode.BadRequest, "note too long")]
    [TestCase(HttpStatusCode.NotFound, "no such map")]
    public void ClientErrorsKeepStatusAndMessage(HttpStatusCode upstream, string message)
    {
        var (status, error) = Run(new HttpRequestException(message, null, upstream));

        Assert.That(status, Is.EqualTo((int)upstream));
        Assert.That(error, Is.EqualTo(message));
    }

    private static (int Status, string Error) Run(Exception exception)
    {
        var context = RunFilter(exception);
        Assert.That(context.ExceptionHandled, Is.True);
        var result = (ObjectResult)context.Result!;
        return (result.StatusCode!.Value, ((ErrorResult)result.Value!).Error);
    }

    private static ExceptionContext RunFilter(Exception exception)
    {
        var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        var context = new ExceptionContext(actionContext, new List<IFilterMetadata>()) { Exception = exception };
        new HttpRequestExceptionFilter().OnException(context);
        return context;
    }
}
