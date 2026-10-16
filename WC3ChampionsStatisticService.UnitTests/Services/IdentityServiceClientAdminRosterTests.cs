using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using W3C.Domain;
using W3ChampionsStatisticService.Services;
using WC3ChampionsStatisticService.Tests.Admin;

namespace WC3ChampionsStatisticService.Tests.Services;

[TestFixture]
[NonParallelizable]
public class IdentityServiceClientAdminRosterTests
{
    private const string SecretHeader = "x-identification-internal-secret";

    private string _previous;

    [SetUp]
    public void Remember() => _previous = Environment.GetEnvironmentVariable(IdentificationInternalSecretProvider.VariableName);

    [TearDown]
    public void Restore() => Environment.SetEnvironmentVariable(IdentificationInternalSecretProvider.VariableName, _previous);

    private sealed class CapturingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    private static (IdentityServiceClient Client, CapturingHandler Handler, RecordingHttpClientFactory Factory) Create(
        string body = "{\"battleTags\":[]}", HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new CapturingHandler(status, body);
        var factory = new RecordingHttpClientFactory(handler);
        return (new IdentityServiceClient(factory), handler, factory);
    }

    [Test]
    public async Task GetAdminBattleTags_SendsSecretHeaderToAdminBattleTagsPath()
    {
        Environment.SetEnvironmentVariable(IdentificationInternalSecretProvider.VariableName, "id-secret");
        var (client, handler, _) = Create("{\"battleTags\":[\"Admin#1\",\"Mod#2\"]}");

        var result = await client.GetAdminBattleTags();

        var request = handler.Requests.Single();
        Assert.AreEqual(HttpMethod.Get, request.Method);
        Assert.AreEqual("/api/permissions/admin-battletags", request.RequestUri.AbsolutePath);
        Assert.AreEqual("id-secret", request.Headers.GetValues(SecretHeader).Single());
        Assert.IsNull(request.Headers.Authorization);
        CollectionAssert.AreEqual(new[] { "Admin#1", "Mod#2" }, result);
    }

    [TestCase(null)]
    [TestCase("")]
    public async Task GetAdminBattleTags_SecretUnset_SendsNoSecretHeader(string value)
    {
        Environment.SetEnvironmentVariable(IdentificationInternalSecretProvider.VariableName, value);
        var (client, handler, _) = Create();

        await client.GetAdminBattleTags();

        Assert.IsFalse(handler.Requests.Single().Headers.Contains(SecretHeader));
    }

    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.InternalServerError)]
    public void GetAdminBattleTags_NonOk_ThrowsHttpRequestException(HttpStatusCode status)
    {
        var (client, _, _) = Create("", status);

        var ex = Assert.ThrowsAsync<HttpRequestException>(() => client.GetAdminBattleTags());

        Assert.AreEqual(status, ex.StatusCode);
    }

    [TestCase("{}")]
    [TestCase("{\"battleTags\":null}")]
    public void GetAdminBattleTags_MissingBattleTags_ThrowsInvalidDataException(string body)
    {
        var (client, _, _) = Create(body);

        Assert.ThrowsAsync<InvalidDataException>(() => client.GetAdminBattleTags());
    }

    [Test]
    public async Task GetAdminBattleTags_UsesTheNamedIdentificationClient()
    {
        var (client, _, factory) = Create();

        await client.GetAdminBattleTags();

        Assert.That(factory.RequestedNames, Is.EqualTo(new[] { HttpClientNames.Identification }));
    }
}
