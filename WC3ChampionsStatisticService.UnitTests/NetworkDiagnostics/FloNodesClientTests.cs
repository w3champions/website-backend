using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using W3C.Domain.MatchmakingService;
using WC3ChampionsStatisticService.Tests.Admin;

namespace WC3ChampionsStatisticService.Tests.NetworkDiagnostics;

[TestFixture]
public class FloNodesClientTests
{
    private const string Body = """
    [{ "id": 12, "name": "US East 4", "ipAddress": "1.2.3.4", "port": 3549, "location": "Ashburn", "countryId": "US",
       "connected": true, "settings": { "isDisabled": false, "isCnOptimized": false }, "proxies": [] },
     { "id": 14, "name": "EU 2", "location": "Falkenstein", "countryId": "DE", "settings": { "isDisabled": true } }]
    """;

    private static MatchmakingServiceClient ClientFor(StubMatchmakingHandler handler) =>
        new(new StubHttpClientFactory(new HttpClient(handler)));

    [Test]
    public async Task GetFloNodes_CallsFloNodesAndMapsFields()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, Body);

        var nodes = await ClientFor(handler).GetFloNodes();

        StringAssert.EndsWith("/flo/nodes", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.AreEqual(2, nodes.Count);
        Assert.AreEqual(12, nodes[0].Id);
        Assert.AreEqual("US East 4", nodes[0].Name);
        Assert.AreEqual("Ashburn", nodes[0].Location);
        Assert.AreEqual("US", nodes[0].CountryId);
        Assert.IsFalse(nodes[0].Settings.IsDisabled);
        Assert.IsTrue(nodes[1].Settings.IsDisabled);
    }

    [Test]
    public async Task GetFloNodes_MissingOrNullOptionalFields_AreTolerated()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK,
            """[{ "id": 1, "name": null, "location": null, "countryId": null, "settings": null }, { "id": 2 }, null]""");

        var nodes = await ClientFor(handler).GetFloNodes();

        Assert.AreEqual(2, nodes.Count);
        Assert.IsNotNull(nodes[0].Settings);
        Assert.IsFalse(nodes[0].Settings.IsDisabled);
        Assert.IsNull(nodes[0].Name);
        Assert.IsNotNull(nodes[1].Settings);
    }

    [Test]
    public async Task GetFloNodes_EmptyBody_ReturnsEmptyList()
    {
        var nodes = await ClientFor(new StubMatchmakingHandler(HttpStatusCode.OK, "")).GetFloNodes();

        Assert.IsEmpty(nodes);
    }

    [Test]
    public void GetFloNodes_UpstreamError_ThrowsHttpRequestException()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.BadGateway, """{"error":"down"}""");

        Assert.ThrowsAsync<HttpRequestException>(() => ClientFor(handler).GetFloNodes());
    }
}
