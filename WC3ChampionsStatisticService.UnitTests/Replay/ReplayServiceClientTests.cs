using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using W3C.Domain.UpdateService;

namespace WC3ChampionsStatisticService.Tests.Replay;

[TestFixture]
public class ReplayServiceClientTests
{
    private const string AdminSecretHeader = "x-admin-secret";

    // ADMIN_SECRET is set explicitly for the whole test run by AdminSecretTestEnvironment.
    private const string ConfiguredSecret = AdminSecretTestEnvironment.Secret;

    // The client disposes each request after sending, so the header values are read inside the handler callback.
    private static (ReplayServiceClient Client, List<Uri> Uris, List<string[]> SecretHeaders) CreateCapturingClient(
        HttpStatusCode status, byte[] body)
    {
        var uris = new List<Uri>();
        var secretHeaders = new List<string[]>();
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) =>
            {
                uris.Add(request.RequestUri);
                secretHeaders.Add(request.Headers.TryGetValues(AdminSecretHeader, out var values) ? values.ToArray() : []);
            })
            .ReturnsAsync(() => new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler.Object));
        return (new ReplayServiceClient(factory.Object), uris, secretHeaders);
    }

    private static void AssertSecretInHeaderAndNotInUrl(Uri uri, string[] secretHeader, string expectedPath)
    {
        Assert.AreEqual(expectedPath, uri.AbsolutePath);
        Assert.AreEqual(string.Empty, uri.Query);
        Assert.IsFalse(uri.ToString().Contains("secret", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(1, secretHeader.Length, "x-admin-secret header is missing");
        Assert.AreEqual(ConfiguredSecret, secretHeader[0]);
        Assert.IsFalse(uri.ToString().Contains(secretHeader[0]));
    }

    [Test]
    public async Task GenerateReplay_SendsAdminSecretHeaderAndNoSecretInUrl()
    {
        var replayBytes = new byte[] { 1, 2, 3, 4 };
        var (client, uris, secretHeaders) = CreateCapturingClient(HttpStatusCode.OK, replayBytes);

        await using var stream = await client.GenerateReplay(42);

        AssertSecretInHeaderAndNotInUrl(uris.Single(), secretHeaders.Single(), "/generate/42");
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        CollectionAssert.AreEqual(replayBytes, copy.ToArray());
    }

    [Test]
    public void GenerateReplay_NonSuccessStatus_ThrowsHttpRequestException()
    {
        var (client, _, _) = CreateCapturingClient(HttpStatusCode.NotFound, []);

        var ex = Assert.ThrowsAsync<HttpRequestException>(async () => await client.GenerateReplay(42));
        Assert.AreEqual(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [TestCase(HttpStatusCode.NotFound)]
    [TestCase(HttpStatusCode.Gone)]
    public void GenerateReplay_AbsentOrArchivedReplay_PropagatesUpstreamStatus(HttpStatusCode status)
    {
        var (client, _, _) = CreateCapturingClient(status, []);

        var ex = Assert.ThrowsAsync<HttpRequestException>(async () => await client.GenerateReplay(42));
        Assert.AreEqual(status, ex.StatusCode);
    }

    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.Forbidden)]
    [TestCase(HttpStatusCode.BadRequest)]
    [TestCase(HttpStatusCode.TooManyRequests)]
    [TestCase(HttpStatusCode.InternalServerError)]
    [TestCase(HttpStatusCode.ServiceUnavailable)]
    public void GenerateReplay_OtherUpstreamFailure_ThrowsBadGatewayKeepingUpstreamStatusInMessage(HttpStatusCode status)
    {
        var (client, _, _) = CreateCapturingClient(status, "secret-upstream-body"u8.ToArray());

        var ex = Assert.ThrowsAsync<HttpRequestException>(async () => await client.GenerateReplay(42));
        Assert.AreEqual(HttpStatusCode.BadGateway, ex.StatusCode);
        StringAssert.Contains($"{(int)status}", ex.Message);
        StringAssert.DoesNotContain("secret-upstream-body", ex.Message);
    }

    [Test]
    public async Task GetChatLogs_SendsAdminSecretHeaderAndNoSecretInUrl()
    {
        var body = "{\"messages\":[],\"events\":[]}"u8.ToArray();
        var (client, uris, secretHeaders) = CreateCapturingClient(HttpStatusCode.OK, body);

        var result = await client.GetChatLogs(42);

        AssertSecretInHeaderAndNotInUrl(uris.Single(), secretHeaders.Single(), "/chats/42");
        Assert.IsNotNull(result);
    }

    [Test]
    public async Task GetChatLogs_GameWithoutChats_ReturnsEmptyData()
    {
        var body = "{\"players\":[],\"messages\":[],\"events\":[]}"u8.ToArray();
        var (client, _, _) = CreateCapturingClient(HttpStatusCode.OK, body);

        var result = await client.GetChatLogs(42);

        Assert.IsNotNull(result);
        Assert.IsEmpty(result.Messages);
    }

    [TestCase(HttpStatusCode.NotFound)]
    [TestCase(HttpStatusCode.Gone)]
    public void GetChatLogs_AbsentOrArchivedReplay_ThrowsWithUpstreamStatus(HttpStatusCode status)
    {
        var body = "{\"message\":\"replay not found\"}"u8.ToArray();
        var (client, _, _) = CreateCapturingClient(status, body);

        var ex = Assert.ThrowsAsync<HttpRequestException>(async () => await client.GetChatLogs(42));
        Assert.AreEqual(status, ex.StatusCode);
    }

    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.Forbidden)]
    [TestCase(HttpStatusCode.BadRequest)]
    [TestCase(HttpStatusCode.TooManyRequests)]
    [TestCase(HttpStatusCode.InternalServerError)]
    [TestCase(HttpStatusCode.ServiceUnavailable)]
    public void GetChatLogs_OtherUpstreamFailure_ThrowsBadGatewayKeepingUpstreamStatusInMessage(HttpStatusCode status)
    {
        var (client, _, _) = CreateCapturingClient(status, "secret-upstream-body"u8.ToArray());

        var ex = Assert.ThrowsAsync<HttpRequestException>(async () => await client.GetChatLogs(42));
        Assert.AreEqual(HttpStatusCode.BadGateway, ex.StatusCode);
        StringAssert.Contains($"{(int)status}", ex.Message);
        StringAssert.DoesNotContain("secret-upstream-body", ex.Message);
    }
}
