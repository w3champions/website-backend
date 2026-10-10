using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using W3C.Domain;
using W3C.Domain.UpdateService;
using WC3ChampionsStatisticService.Tests.Admin;

namespace WC3ChampionsStatisticService.Tests.UpdateService;

[TestFixture]
public class UpdateServiceClientTests
{
    private sealed class PerNameFactory(Dictionary<string, HttpMessageHandler> handlers) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handlers[name], disposeHandler: false);
    }

    private static (UpdateServiceClient Client, StubMatchmakingHandler Regular, StubMatchmakingHandler Upload) Create()
    {
        var regular = new StubMatchmakingHandler(HttpStatusCode.OK, "{\"id\":\"f1\"}");
        var upload = new StubMatchmakingHandler(HttpStatusCode.OK, "{\"id\":\"f1\"}");
        var factory = new PerNameFactory(new()
        {
            [HttpClientNames.Update] = regular,
            [HttpClientNames.UpdateUpload] = upload,
        });
        return (new UpdateServiceClient(factory), regular, upload);
    }

    [Test]
    public async Task MapUpload_UsesTheLongTimeoutUploadClient()
    {
        var (client, regular, upload) = Create();

        await client.CreateMapFromFormAsync(new HttpRequestMessage { Content = new StringContent("map-bytes") });

        Assert.That(upload.Requests, Has.Count.EqualTo(1));
        Assert.That(regular.Requests, Is.Empty);
    }

    [Test]
    public async Task OtherCalls_UseTheRegularClient()
    {
        var (client, regular, upload) = Create();

        await client.GetMapFile("f1");

        Assert.That(regular.Requests, Has.Count.EqualTo(1));
        Assert.That(upload.Requests, Is.Empty);
    }
}
