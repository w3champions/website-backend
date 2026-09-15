using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using W3ChampionsStatisticService.WebApi.ExceptionFilters;
using WC3ChampionsStatisticService.Tests.Maps;

namespace WC3ChampionsStatisticService.Tests.WebApi;

/// <summary>
/// The global answer to an HttpRequestException: its status (500 without one) and an ErrorResult, logged server-side.
/// A status-less one is a transport failure whose message names the upstream host and port, so a fixed text replaces
/// it. Log entries carry the action and the status only: an exception message can hold an upstream body, and the
/// request URL can hold a proofHash.
/// </summary>
[TestFixture]
public class HttpRequestExceptionFilterTests
{
    private const string Action = "W3ChampionsStatisticService.Tournaments.TournamentsController.GetEnabledFloNodes (W3ChampionsStatisticService)";
    private const string InternalHost = "mm.internal.example:3000";
    private const string UpstreamText = "upstream body text";

    [Test]
    public void TransportFailure_AnswersAFixedMessage_AndLogsTheExceptionAtError()
    {
        var logger = new Mock<ILogger<HttpRequestExceptionFilter>>();
        var exception = new HttpRequestException($"Connection refused ({InternalHost})", new SocketException(111));
        var context = ExceptionContextFor(exception);

        new HttpRequestExceptionFilter(logger.Object).OnException(context);

        Assert.That(context.ExceptionHandled, Is.True);
        var result = (ObjectResult)context.Result!;
        Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Status500InternalServerError));
        Assert.That(((ErrorResult)result.Value!).Error, Is.EqualTo(HttpRequestExceptionFilter.TransportFailureMessage));
        Assert.That(HttpRequestExceptionFilter.TransportFailureMessage, Does.Not.Contain("internal"));
        var entry = LogEntries(logger).Single();
        Assert.That(entry.Level, Is.EqualTo(LogLevel.Error));
        Assert.That(entry.Exception, Is.SameAs(exception), "a transport failure's text is diagnostic and holds no upstream body");
        Assert.That(entry.Message, Does.Contain(Action));
    }

    [TestCase(HttpStatusCode.BadGateway, LogLevel.Error)]
    [TestCase(HttpStatusCode.InternalServerError, LogLevel.Error)]
    [TestCase(HttpStatusCode.OK, LogLevel.Error)]
    [TestCase(HttpStatusCode.Forbidden, LogLevel.Warning)]
    [TestCase(HttpStatusCode.NotFound, LogLevel.Warning)]
    public void StatusBearingFailure_KeepsItsStatusAndMessage_AndLogsOnlyTheActionAndStatus(HttpStatusCode status, LogLevel level)
    {
        var logger = new Mock<ILogger<HttpRequestExceptionFilter>>();
        var context = ExceptionContextFor(new HttpRequestException(UpstreamText, null, status));

        new HttpRequestExceptionFilter(logger.Object).OnException(context);

        Assert.That(context.ExceptionHandled, Is.True);
        var result = (ObjectResult)context.Result!;
        Assert.That(result.StatusCode, Is.EqualTo((int)status));
        Assert.That(((ErrorResult)result.Value!).Error, Is.EqualTo(UpstreamText));
        var entry = LogEntries(logger).Single();
        Assert.That(entry.Level, Is.EqualTo(level));
        Assert.That(entry.Exception, Is.Null);
        Assert.That(entry.Message, Does.Contain(Action).And.Contain(((int)status).ToString()));
        Assert.That(entry.Message, Does.Not.Contain(UpstreamText));
    }

    [TestCase(null)]
    [TestCase(HttpStatusCode.NotFound)]
    [TestCase(HttpStatusCode.BadGateway)]
    public void LogEntries_NeverCarryTheRequestUrl(HttpStatusCode? status)
    {
        var logger = new Mock<ILogger<HttpRequestExceptionFilter>>();
        var context = ExceptionContextFor(new HttpRequestException("failed", null, status));
        context.HttpContext.Request.Path = "/api/maps/temporary/status";
        context.HttpContext.Request.QueryString = new QueryString("?proofHash=" + TemporaryMapClientTests.ProofHash);

        new HttpRequestExceptionFilter(logger.Object).OnException(context);

        var entries = LogEntries(logger);
        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries.Any(e => e.Message.Contains(TemporaryMapClientTests.ProofHash) || e.Message.Contains("/api/maps")), Is.False);
    }

    [Test]
    public void OtherExceptions_AreLeftToTheNextHandler()
    {
        var logger = new Mock<ILogger<HttpRequestExceptionFilter>>();
        var context = ExceptionContextFor(new InvalidOperationException("not an upstream failure"));

        new HttpRequestExceptionFilter(logger.Object).OnException(context);

        Assert.That(context.ExceptionHandled, Is.False);
        Assert.That(context.Result, Is.Null);
        Assert.That(LogEntries(logger), Is.Empty);
    }

    private static ExceptionContext ExceptionContextFor(Exception exception)
    {
        var actionContext = new ActionContext(
            new DefaultHttpContext(), new RouteData(), new ActionDescriptor { DisplayName = Action });
        return new ExceptionContext(actionContext, new List<IFilterMetadata>()) { Exception = exception };
    }

    internal static List<(LogLevel Level, string Message, Exception Exception)> LogEntries<T>(Mock<ILogger<T>> logger)
        => logger.Invocations
            .Where(i => i.Method.Name == nameof(ILogger.Log))
            .Select(i => ((LogLevel)i.Arguments[0], i.Arguments[2]?.ToString() ?? "", (Exception)i.Arguments[3]))
            .ToList();
}
