using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using W3ChampionsStatisticService.Services;

namespace WC3ChampionsStatisticService.Tests.Services;

[TestFixture]
public class IdentityServiceClientTests
{
    [Test]
    public async Task ResolveCanonicalBattleTag_UserExists_ReturnsCanonicalIdFromBody()
    {
        // The endpoint returns 200 with body { "id": "TORREN#11438" } per Spec 1's contract.
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\":\"TORREN#11438\"}", System.Text.Encoding.UTF8, "application/json")
            });

        var client = new IdentityServiceClient(new HttpClient(handler.Object));
        var canonical = await client.ResolveCanonicalBattleTag("torren#11438");

        Assert.AreEqual("TORREN#11438", canonical);
    }

    [Test]
    public async Task ResolveCanonicalBattleTag_UserNotFound_ReturnsNull()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NotFound));

        var client = new IdentityServiceClient(new HttpClient(handler.Object));
        var canonical = await client.ResolveCanonicalBattleTag("nonexistent#9999");

        Assert.IsNull(canonical);
    }

    [Test]
    public void ResolveCanonicalBattleTag_ServerError_ThrowsHttpRequestException()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var client = new IdentityServiceClient(new HttpClient(handler.Object));

        Assert.ThrowsAsync<HttpRequestException>(async () =>
            await client.ResolveCanonicalBattleTag("torren#11438"));
    }
}
