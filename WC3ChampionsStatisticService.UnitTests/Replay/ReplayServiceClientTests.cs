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
