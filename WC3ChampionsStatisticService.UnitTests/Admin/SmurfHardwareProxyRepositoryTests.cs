using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using MongoDB.Driver;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using W3C.Domain;
using W3ChampionsStatisticService.Admin;

namespace WC3ChampionsStatisticService.Tests.Admin;

[TestFixture]
public class SmurfHardwareProxyRepositoryTests
{
    // MongoClient connects lazily; these tests never touch the database.
    private static readonly MongoClient UnusedMongo = new("mongodb://localhost:1");

    private const string Tag = "Name#1234";
    private const string Other = "Other#9999";
    private const string Hash = "0a1b2c3d";

    private static (AdminRepository Repository, StubMatchmakingHandler Handler, RecordingHttpClientFactory Factory) Create(HttpStatusCode status = HttpStatusCode.OK, string body = "{}")
    {
        var handler = new StubMatchmakingHandler(status, body);
        var factory = new RecordingHttpClientFactory(handler);
        return (new AdminRepository(UnusedMongo, factory), handler, factory);
    }

    private static readonly object[] Cases =
    [
        new object[] { "GET", "/admin/smurf-detection/hardware/links/Name%231234", false, false },
        new object[] { "GET", "/admin/smurf-detection/hardware/items/Name%231234", false, false },
        new object[] { "GET", "/admin/smurf-detection/hardware/items/Name%231234/0a1b2c3d", false, false },
        new object[] { "POST", "/admin/smurf-detection/links/Name%231234/Other%239999/smurf", true, true },
        new object[] { "DELETE", "/admin/smurf-detection/links/Name%231234/Other%239999/smurf", true, false },
        new object[] { "POST", "/admin/smurf-detection/links/Name%231234/Other%239999/not-smurf", true, true },
        new object[] { "DELETE", "/admin/smurf-detection/links/Name%231234/Other%239999/not-smurf", true, false },
        new object[] { "POST", "/admin/smurf-detection/hardware/items/Name%231234/0a1b2c3d/trust", true, true },
        new object[] { "DELETE", "/admin/smurf-detection/hardware/items/Name%231234/0a1b2c3d/trust", true, false },
        new object[] { "POST", "/admin/smurf-detection/hardware/items/Name%231234/0a1b2c3d/ignore", true, true },
        new object[] { "DELETE", "/admin/smurf-detection/hardware/items/Name%231234/0a1b2c3d/ignore", true, false },
    ];

    private static Task<MatchmakingProxyResult> Invoke(AdminRepository r, int index, string reason, string author) => index switch
    {
        0 => r.GetHardwareLinks(Tag),
        1 => r.GetHardwareItems(Tag),
        2 => r.GetHardwareItem(Tag, Hash),
        3 => r.SetSmurfVerdict(Tag, Other, reason, author),
        4 => r.RevokeSmurfVerdict(Tag, Other, author),
        5 => r.SetNotSmurfVerdict(Tag, Other, reason, author),
        6 => r.RevokeNotSmurfVerdict(Tag, Other, author),
        7 => r.TrustHardwareItem(Tag, Hash, reason, author),
        8 => r.RevokeHardwareItemTrust(Tag, Hash, author),
        9 => r.IgnoreHardwareItem(Tag, Hash, reason, author),
        10 => r.RevokeHardwareItemIgnore(Tag, Hash, author),
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    [Test]
    public async Task EveryRoute_UsesMethodEncodedPath_SecretHeader_AndNamedClient([Range(0, 10)] int index)
    {
        var (repository, handler, factory) = Create();
        var expected = (object[])Cases[index];

        await Invoke(repository, index, "because", "Mod#1");

        var request = handler.Requests.Single();
        Assert.That(request.Method.Method, Is.EqualTo((string)expected[0]));
        Assert.That(request.RequestUri!.AbsolutePath, Does.EndWith((string)expected[1]));
        Assert.That(request.RequestUri.AbsoluteUri, Does.Not.Contain("#"));
        Assert.That(request.Headers.GetValues(AdminSecretProvider.HeaderName).Single(), Is.EqualTo(AdminSecretTestEnvironment.Secret));
        Assert.That(factory.RequestedNames, Is.All.EqualTo(HttpClientNames.Matchmaking));
    }

    [Test]
    public async Task WriteRoutes_SendAuthorAndReasonInTheBody([Range(3, 10)] int index)
    {
        var (repository, handler, _) = Create();
        var expected = (object[])Cases[index];

        await Invoke(repository, index, "because", "Mod#1");

        var body = JObject.Parse(handler.RequestBodies.Single());
        Assert.That(body["author"]!.Value<string>(), Is.EqualTo("Mod#1"));
        if ((bool)expected[3])
            Assert.That(body["reason"]!.Value<string>(), Is.EqualTo("because"));
        else
            Assert.That(body.ContainsKey("reason"), Is.False);
    }

    [Test]
    public async Task ReadRoutes_SendNoBody([Range(0, 2)] int index)
    {
        var (repository, handler, _) = Create();

        await Invoke(repository, index, null, null);

        Assert.That(handler.RequestBodies.Single(), Is.Empty);
    }

    [Test]
    public async Task Audit_ForwardsBattleTagBeforeAndLimitEncoded_AndOmitsAbsentOnes()
    {
        var (repository, handler, _) = Create(HttpStatusCode.OK, "{\"entries\":[],\"next\":null}");

        await repository.GetHardwareAudit(Tag, "a+b/c=", 50);
        await repository.GetHardwareAudit(null, null, null);

        var full = handler.Requests[0].RequestUri!;
        Assert.That(full.AbsolutePath, Does.EndWith("/admin/smurf-detection/hardware/audit"));
        Assert.That(full.Query, Is.EqualTo("?battleTag=Name%231234&before=a%2Bb%2Fc%3D&limit=50"));
        Assert.That(handler.Requests[1].RequestUri!.Query, Is.Empty);
    }

    [TestCase(HttpStatusCode.OK, "{\"ok\":true}")]
    [TestCase(HttpStatusCode.BadRequest, "{\"errors\":[{\"msg\":\"bad\"}]}")]
    [TestCase(HttpStatusCode.NotFound, "{\"error\":\"nope\"}")]
    [TestCase(HttpStatusCode.Conflict, "{\"error\":\"conflict\"}")]
    [TestCase(HttpStatusCode.InternalServerError, "{\"error\":\"boom\"}")]
    public async Task MatchmakingStatusAndJsonBody_PassThroughUnchanged(HttpStatusCode status, string body)
    {
        var (repository, _, _) = Create(status, body);

        var result = await repository.GetHardwareLinks(Tag);

        Assert.That(result.StatusCode, Is.EqualTo((int)status));
        Assert.That(result.Body, Is.EqualTo(body));
    }

    [Test]
    public async Task Matchmaking403_BecomesGeneric502_WithoutTheUpstreamBody()
    {
        var (repository, _, _) = Create(HttpStatusCode.Forbidden, "{\"error\":\"bad x-admin-secret\"}");

        var result = await repository.GetHardwareLinks(Tag);

        Assert.That(result.StatusCode, Is.EqualTo(502));
        Assert.That(result.Body, Does.Not.Contain("secret"));
        Assert.That(JObject.Parse(result.Body)["error"]!.Value<string>(), Is.EqualTo(MatchmakingProxyResult.UnavailableMessage));
    }

    [TestCase(typeof(HttpRequestException))]
    [TestCase(typeof(TaskCanceledException))]
    public async Task NetworkFailure_BecomesGeneric502(Type exceptionType)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType, "connect to https://mm.internal failed")!;
        var repository = new AdminRepository(UnusedMongo, new RecordingHttpClientFactory(new ThrowingMatchmakingHandler(exception)));

        var result = await repository.RevokeHardwareItemTrust(Tag, Hash, "Mod#1");

        Assert.That(result.StatusCode, Is.EqualTo(502));
        Assert.That(result.Body, Does.Not.Contain("mm.internal"));
    }
}
