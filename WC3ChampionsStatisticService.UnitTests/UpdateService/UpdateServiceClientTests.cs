using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using W3C.Domain;
using W3C.Domain.UpdateService;
using W3C.Domain.UpdateService.Contracts;
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
            [HttpClientNames.UpdateMapMetadataRecompute] = new StubMatchmakingHandler(HttpStatusCode.OK, "{}"),
        });
        return (new UpdateServiceClient(factory), regular, upload);
    }

    [Test]
    public async Task MapMetadataRecompute_UsesItsOwnLongTimeoutClient_AndTheContractBody()
    {
        var recompute = new StubMatchmakingHandler(HttpStatusCode.OK, "{\"dryRun\":true,\"results\":[]}");
        var regular = new StubMatchmakingHandler();
        var factory = new PerNameFactory(new()
        {
            [HttpClientNames.Update] = regular,
            [HttpClientNames.UpdateUpload] = new StubMatchmakingHandler(),
            [HttpClientNames.UpdateMapMetadataRecompute] = recompute,
        });

        var response = await new UpdateServiceClient(factory).RecomputeMapMetadata(
            new MapMetadataRecomputeRequest { DryRun = true, Paths = ["W3Champions/x.w3x"] });

        Assert.That(response.DryRun, Is.True);
        Assert.That(regular.Requests, Is.Empty);
        Assert.That(recompute.Requests, Has.Count.EqualTo(1));
        Assert.That(recompute.Requests[0].Method, Is.EqualTo(HttpMethod.Post));
        Assert.That(recompute.Requests[0].RequestUri!.AbsolutePath, Is.EqualTo("/api/content/maps/metadata/recompute"));
        Assert.That(recompute.Requests[0].Headers.GetValues(AdminSecretProvider.HeaderName), Is.EqualTo(new[] { AdminSecretTestEnvironment.Secret }));
        Assert.That(recompute.RequestBodies[0], Is.EqualTo("{\"dryRun\":true,\"paths\":[\"W3Champions/x.w3x\"]}"));
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
