using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.Extensions;

namespace WC3ChampionsStatisticService.Tests.Extensions;

[TestFixture]
public class HttpResponseMessageHandleErrorExtensionTests
{
    private static HttpResponseMessage Response(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Test]
    public void ClientErrorSurfacesUpstreamMessageAndKeepsStatusCode()
    {
        using var response = Response(HttpStatusCode.BadRequest, "{\"error\":\"x\"}");

        var ex = Assert.ThrowsAsync<HttpRequestException>(async () => await response.ThrowIfError());

        Assert.That(ex!.Message, Is.EqualTo("x"));
        Assert.That(ex.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public void ServerErrorDoesNotLeakUpstreamMessage()
    {
        using var response = Response(HttpStatusCode.InternalServerError, "{\"error\":\"connect ECONNREFUSED mongo-internal:27017\"}");

        var ex = Assert.ThrowsAsync<HttpRequestException>(async () => await response.ThrowIfError());

        Assert.That(ex!.Message, Does.Not.Contain("mongo-internal"));
        Assert.That(ex.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
    }

    [TestCase("<html>bad gateway</html>")]
    [TestCase("{\"error\":{\"code\":1}}")]
    [TestCase("")]
    public void NonStandardBodyStillThrowsWithStatusCode(string body)
    {
        using var response = Response(HttpStatusCode.BadRequest, body);

        var ex = Assert.ThrowsAsync<HttpRequestException>(async () => await response.ThrowIfError());

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task OkDoesNotThrow()
    {
        using var response = Response(HttpStatusCode.OK, "{}");

        await response.ThrowIfError();
    }
}
