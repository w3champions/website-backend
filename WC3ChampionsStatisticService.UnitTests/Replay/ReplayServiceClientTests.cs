using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using W3C.Domain.UpdateService;

namespace WC3ChampionsStatisticService.Tests.Replay;

[TestFixture]
public class ReplayServiceClientTests
{
    [Test]
    public async Task AMissingChatLogComesBackAsNullRatherThanAnEmptyObject()
    {
        var client = ClientReturning(HttpStatusCode.NotFound, "not found");

        var chats = await client.GetChatLogs(4242);

        Assert.That(chats, Is.Null);
    }

    [Test]
    public async Task AMissingReplayComesBackAsNullRatherThanThrowing()
    {
        var client = ClientReturning(HttpStatusCode.NotFound, "not found");

        var replay = await client.GenerateReplay(4242);

        Assert.That(replay, Is.Null);
    }

    [Test]
    public async Task AnArchivedReplayComesBackAsNull()
    {
        var client = ClientReturning(HttpStatusCode.Gone, "archived");

        var replay = await client.GenerateReplay(4242);

        Assert.That(replay, Is.Null);
    }

    [Test]
    public async Task AnArchivedChatLogComesBackAsNull()
    {
        var client = ClientReturning(HttpStatusCode.Gone, "archived");

        var chats = await client.GetChatLogs(4242);

        Assert.That(chats, Is.Null);
    }

    // A 5xx (or any other non-2xx that isn't NotFound/Gone) is a genuine upstream
    // failure, not "no replay". It must not be swallowed into a null -- a moderator
    // seeing "replay unavailable" for an outage instead of an error is worse than a 500.
    [Test]
    public void AnUpstreamOutageGeneratingAReplayThrowsRatherThanReturningNull()
    {
        var client = ClientReturning(HttpStatusCode.InternalServerError, "boom");

        Assert.ThrowsAsync<HttpRequestException>(() => client.GenerateReplay(4242));
    }

    [Test]
    public void AnUpstreamOutageFetchingChatLogsThrowsRatherThanReturningNull()
    {
        var client = ClientReturning(HttpStatusCode.BadGateway, "boom");

        Assert.ThrowsAsync<HttpRequestException>(() => client.GetChatLogs(4242));
    }

    private static ReplayServiceClient ClientReturning(HttpStatusCode status, string body)
    {
        var handler = new StubHandler(status, body);
        return new ReplayServiceClient(new TestHttpClientFactory(new HttpClient(handler)));
    }

    private class TestHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }
}
