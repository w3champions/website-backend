using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.WebApi.ExceptionFilters;

namespace WC3ChampionsStatisticService.Tests.Admin;

[TestFixture]
public class MatchmakingPassthroughExceptionFilterTests
{
    private const string InternalBody = """{"error":"Matchmaking service error","code":"INTERNAL"}""";

    [TestCase(HttpStatusCode.BadRequest, """{"error":"endsAt: end-before-start","code":"INVALID_FIELD","field":"endsAt","rule":"end-before-start"}""")]
    [TestCase(HttpStatusCode.NotFound, """{"error":"UNKNOWN_EVENT","code":"UNKNOWN_EVENT"}""")]
    [TestCase(HttpStatusCode.Conflict, """{"error":"ROLE_EXISTS","code":"ROLE_EXISTS","data":{"battleTag":"Grubby#1234","role":"host"}}""")]
    [TestCase(HttpStatusCode.Forbidden, """{"errors":[{"msg":"Permission denied"}]}""")]
    public void ClientErrorsPassTheMatchmakingBodyThroughVerbatim(HttpStatusCode status, string body)
    {
        var context = RunFilter(new MatchmakingPassthroughException(status, body));

        Assert.That(context.ExceptionHandled, Is.True);
        var result = (ContentResult)context.Result!;
        Assert.That(result.StatusCode, Is.EqualTo((int)status));
        Assert.That(result.ContentType, Is.EqualTo("application/json"));
        Assert.That(result.Content, Is.EqualTo(body));
    }

    [TestCase(HttpStatusCode.InternalServerError, "{\"error\":\"connect ECONNREFUSED mongo-internal:27017\"}")]
    [TestCase(HttpStatusCode.BadGateway, "<html>502 Bad Gateway</html>")]
    [TestCase(HttpStatusCode.GatewayTimeout, null)]
    public void ServerErrorsAnswerTheGenericInternalBody(HttpStatusCode status, string body)
    {
        var context = RunFilter(new MatchmakingPassthroughException(status, body));

        Assert.That(context.ExceptionHandled, Is.True);
        var result = (ContentResult)context.Result!;
        Assert.That(result.StatusCode, Is.EqualTo((int)status));
        Assert.That(result.ContentType, Is.EqualTo("application/json"));
        Assert.That(result.Content, Is.EqualTo(InternalBody));
    }

    [TestCase(HttpStatusCode.NotFound, "<!DOCTYPE html><pre>Cannot GET /admin/commercial-events/x</pre>", "Not Found")]
    [TestCase(HttpStatusCode.BadRequest, "", "Bad Request")]
    [TestCase(HttpStatusCode.BadRequest, null, "Bad Request")]
    [TestCase(HttpStatusCode.BadRequest, "[1,2]", "Bad Request")]
    [TestCase((HttpStatusCode)477, "<html>unknown status</html>", "Matchmaking service error")]
    public void ClientErrorsWithoutAJsonObjectBodyAnswerTheReasonPhrase(HttpStatusCode status, string body, string reasonPhrase)
    {
        var context = RunFilter(new MatchmakingPassthroughException(status, body));

        Assert.That(context.ExceptionHandled, Is.True);
        var result = (ContentResult)context.Result!;
        Assert.That(result.StatusCode, Is.EqualTo((int)status));
        var json = JObject.Parse(result.Content!);
        Assert.That(json["error"]!.Value<string>(), Is.EqualTo(reasonPhrase));
        Assert.That(json.ContainsKey("code"), Is.False);
    }

    [Test]
    public void OtherExceptionsAreLeftToTheOtherFilters()
    {
        var context = RunFilter(new HttpRequestException("not ours", null, HttpStatusCode.NotFound));

        Assert.That(context.ExceptionHandled, Is.False);
        Assert.That(context.Result, Is.Null);
    }

    private static ExceptionContext RunFilter(Exception exception)
    {
        var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        var context = new ExceptionContext(actionContext, new List<IFilterMetadata>()) { Exception = exception };
        new MatchmakingPassthroughExceptionFilter().OnException(context);
        return context;
    }
}
