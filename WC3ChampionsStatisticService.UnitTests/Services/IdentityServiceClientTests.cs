using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using W3C.Contracts.Admin.Permission;
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

    private const string Token = "header.payload.signature";
    private const string ExpectedPermissionJson = "{\"Id\":\"Admin#1234\",\"BattleTag\":\"Admin#1234\",\"Description\":\"desc\",\"Permissions\":[1],\"Author\":\"Boss#1\"}";

    // The client disposes each request after sending, so the request body is read inside the handler callback.
    private static (IdentityServiceClient Client, List<HttpRequestMessage> Requests, List<string> Bodies) CreateCapturingClient(string body = "[]")
    {
        var requests = new List<HttpRequestMessage>();
        var bodies = new List<string>();
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) =>
            {
                requests.Add(request);
                bodies.Add(request.Content?.ReadAsStringAsync().GetAwaiter().GetResult());
            })
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            });
        return (new IdentityServiceClient(new HttpClient(handler.Object)), requests, bodies);
    }

    private static void AssertBearerAndNoTokenInUrl(HttpRequestMessage request, HttpMethod method)
    {
        Assert.AreEqual(method, request.Method);
        Assert.AreEqual("/api/permissions", request.RequestUri.AbsolutePath);
        Assert.IsNotNull(request.Headers.Authorization, "Authorization header is missing");
        Assert.AreEqual("Bearer", request.Headers.Authorization.Scheme);
        Assert.AreEqual(Token, request.Headers.Authorization.Parameter);
        Assert.IsFalse(request.RequestUri.Query.Contains("authorization", System.StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(request.RequestUri.ToString().Contains(Token));
    }

    [Test]
    public async Task GetPermissions_SendsBearerHeaderAndNoTokenInUrl()
    {
        var (client, requests, bodies) = CreateCapturingClient();

        await client.GetPermissions(Token);

        AssertBearerAndNoTokenInUrl(requests.Single(), HttpMethod.Get);
        Assert.AreEqual(string.Empty, requests.Single().RequestUri.Query);
    }

    [Test]
    public async Task AddAdmin_SendsBearerHeaderAndNoTokenInUrl()
    {
        var (client, requests, bodies) = CreateCapturingClient();

        var permission = new Permission { BattleTag = "Admin#1234", Description = "desc", Permissions = [EPermission.Moderation], Author = "Boss#1" };

        await client.AddAdmin(permission, Token);

        AssertBearerAndNoTokenInUrl(requests.Single(), HttpMethod.Post);
        Assert.AreEqual(ExpectedPermissionJson, bodies.Single());
    }

    [Test]
    public async Task EditAdmin_SendsBearerHeaderAndNoTokenInUrl()
    {
        var (client, requests, bodies) = CreateCapturingClient();

        var permission = new Permission { BattleTag = "Admin#1234", Description = "desc", Permissions = [EPermission.Moderation], Author = "Boss#1" };

        await client.EditAdmin(permission, Token);

        AssertBearerAndNoTokenInUrl(requests.Single(), HttpMethod.Put);
        Assert.AreEqual(ExpectedPermissionJson, bodies.Single());
    }

    [Test]
    public async Task DeleteAdmin_SendsBearerHeaderKeepsEncodedIdAndNoTokenInUrl()
    {
        var (client, requests, bodies) = CreateCapturingClient();

        await client.DeleteAdmin("Some Admin#1234", Token);

        var request = requests.Single();
        AssertBearerAndNoTokenInUrl(request, HttpMethod.Delete);
        Assert.AreEqual("?id=Some+Admin%231234", request.RequestUri.Query);
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
