using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using NUnit.Framework;
using W3ChampionsStatisticService.Admin;

namespace WC3ChampionsStatisticService.Tests.Admin;

/// <summary>The upstream Retry-After header reaches the browser through the shared smurf moderation proxy plumbing.</summary>
[TestFixture]
public class SmurfRetryAfterForwardingTests
{
    private const string Body = "{\"error\":\"placeholder\"}";
    private const string HttpDate = "Wed, 21 Oct 2026 07:28:00 GMT";

    // MongoClient connects lazily; these tests never touch the database.
    private static readonly MongoClient UnusedMongo = new("mongodb://localhost:1");

    private static Task<MatchmakingProxyResult> EventsCall(HttpStatusCode status, string retryAfter) =>
        new AdminRepository(UnusedMongo, new RecordingHttpClientFactory(new StubMatchmakingHandler(status, Body, retryAfter))).GetSmurfEvent("ev-1");

    private static Task<MatchmakingProxyResult> HardwareCall(HttpStatusCode status, string retryAfter) =>
        new AdminRepository(UnusedMongo, new RecordingHttpClientFactory(new StubMatchmakingHandler(status, Body, retryAfter))).GetHardwareLinks("Player#1234");

    private static async Task<(ContentResult Content, IHeaderDictionary Headers)> Render(MatchmakingProxyResult result)
    {
        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().AddLogging().AddMvcCore().Services.BuildServiceProvider() };
        var action = SmurfModerationResults.Forward(result);
        await action.ExecuteResultAsync(new ActionContext(context, new RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()));
        return ((ContentResult)action, context.Response.Headers);
    }

    [TestCase("120")]
    [TestCase("0")]
    [TestCase(HttpDate)]
    public async Task Repository_Busy429WithValidRetryAfter_KeepsTheValueAsSent(string value)
    {
        Assert.That((await EventsCall(HttpStatusCode.TooManyRequests, value)).RetryAfter, Is.EqualTo(value));
        Assert.That((await HardwareCall(HttpStatusCode.TooManyRequests, value)).RetryAfter, Is.EqualTo(value));
    }

    [Test]
    public async Task Repository_Busy429WithoutRetryAfter_HasNone()
    {
        Assert.That((await EventsCall(HttpStatusCode.TooManyRequests, null)).RetryAfter, Is.Null);
        Assert.That((await HardwareCall(HttpStatusCode.TooManyRequests, null)).RetryAfter, Is.Null);
    }

    [TestCase("soon")]
    [TestCase("-5")]
    [TestCase("1.5")]
    [TestCase("")]
    [TestCase("12 seconds")]
    public async Task Repository_MalformedRetryAfter_IsDropped(string value)
    {
        Assert.That((await EventsCall(HttpStatusCode.TooManyRequests, value)).RetryAfter, Is.Null);
        Assert.That((await HardwareCall(HttpStatusCode.TooManyRequests, value)).RetryAfter, Is.Null);
    }

    [TestCase(HttpStatusCode.ServiceUnavailable)]
    [TestCase(HttpStatusCode.OK)]
    public async Task Repository_RetryAfterOnOtherStatuses_IsKeptAsSent(HttpStatusCode status)
    {
        var result = await EventsCall(status, "30");

        Assert.That((result.StatusCode, result.Body, result.RetryAfter), Is.EqualTo(((int)status, Body, "30")));
    }

    [Test]
    public async Task Repository_Forbidden_NeverCarriesRetryAfter()
    {
        var result = await EventsCall(HttpStatusCode.Forbidden, "30");

        Assert.That(result.StatusCode, Is.EqualTo(502));
        Assert.That(result.RetryAfter, Is.Null);
    }

    [Test]
    public async Task Forward_WithRetryAfter_SetsTheHeaderAndKeepsStatusAndBody()
    {
        var (content, headers) = await Render(new MatchmakingProxyResult(429, Body, "45"));

        Assert.That(headers["Retry-After"].ToString(), Is.EqualTo("45"));
        Assert.That((content.StatusCode, content.Content, content.ContentType), Is.EqualTo((429, Body, "application/json")));
    }

    [Test]
    public async Task Forward_WithoutRetryAfter_SetsNoHeader()
    {
        var (content, headers) = await Render(new MatchmakingProxyResult(429, Body));

        Assert.That(headers.ContainsKey("Retry-After"), Is.False);
        Assert.That((content.StatusCode, content.Content), Is.EqualTo((429, Body)));
    }

    [Test]
    public async Task Forward_OtherStatusWithRetryAfter_SetsTheHeaderAsSent()
    {
        var (content, headers) = await Render(new MatchmakingProxyResult(503, Body, HttpDate));

        Assert.That(headers["Retry-After"].ToString(), Is.EqualTo(HttpDate));
        Assert.That(content.StatusCode, Is.EqualTo(503));
    }
}
