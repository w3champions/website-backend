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
public class SmurfEventsProxyRepositoryTests
{
    // MongoClient connects lazily; these tests never touch the database.
    private static readonly MongoClient UnusedMongo = new("mongodb://localhost:1");

    private const string Base = "/admin/smurf-detection/events";
    private static readonly string[] Anchors = ["PlayerA#1234", "PlayerB#5678"];

    private static (AdminRepository Repository, StubMatchmakingHandler Handler, RecordingHttpClientFactory Factory) Create(HttpStatusCode status = HttpStatusCode.OK, string body = "{}")
    {
        var handler = new StubMatchmakingHandler(status, body);
        var factory = new RecordingHttpClientFactory(handler);
        return (new AdminRepository(UnusedMongo, factory), handler, factory);
    }

    // The request path without any path prefix MATCHMAKING_API may carry.
    private static string PathOf(HttpRequestMessage request)
    {
        var configured = Environment.GetEnvironmentVariable("MATCHMAKING_API");
        var prefix = configured == null ? "" : new Uri(configured).AbsolutePath.TrimEnd('/');
        return request.RequestUri!.AbsolutePath[prefix.Length..];
    }

    private static readonly (string Method, string Path)[] Expected =
    [
        ("GET", Base),
        ("GET", Base + "/ev-1"),
        ("GET", Base + "/ev-1/deny-preview"),
        ("POST", Base + "/preview"),
        ("POST", Base),
        ("PUT", Base + "/ev-1"),
        ("DELETE", Base + "/ev-1"),
        ("POST", Base + "/ev-1/review"),
        ("POST", Base + "/ev-1/rename"),
        ("GET", Base + "/ev-1/identifiers"),
        ("GET", Base + "/ev-1/history"),
    ];

    private static Task<MatchmakingProxyResult> Invoke(AdminRepository r, int index) => index switch
    {
        0 => r.GetSmurfEvents(null, null, null, null),
        1 => r.GetSmurfEvent("ev-1"),
        2 => r.GetSmurfEventDenyPreview("ev-1"),
        3 => r.PreviewSmurfEvent("Name", "2026-01-01", "2026-01-02", Anchors, null),
        4 => r.CreateSmurfEvent("Name", "2026-01-01", "2026-01-02", Anchors, "why", "Mod#1"),
        5 => r.UpdateSmurfEvent("ev-1", "Name", "2026-01-01", "2026-01-02", Anchors, "why", "Mod#1"),
        6 => r.DeleteSmurfEvent("ev-1", "why", "Mod#1"),
        7 => r.ReviewSmurfEvent("ev-1", "confirmed", "why", "Sofia LAN", "Mod#1"),
        8 => r.RenameSmurfEvent("ev-1", "Sofia LAN", null, "Mod#1"),
        9 => r.GetSmurfEventIdentifiers("ev-1", false),
        10 => r.GetSmurfEventHistory("ev-1"),
        _ => throw new System.ArgumentOutOfRangeException(nameof(index)),
    };

    [Test]
    public async Task EveryRoute_UsesMethodPath_SecretHeader_AndNamedClient([Range(0, 10)] int index)
    {
        var (repository, handler, factory) = Create();

        await Invoke(repository, index);

        var request = handler.Requests.Single();
        Assert.That(request.Method.Method, Is.EqualTo(Expected[index].Method));
        Assert.That(PathOf(request), Is.EqualTo(Expected[index].Path));
        Assert.That(request.Headers.GetValues(AdminSecretProvider.HeaderName).Single(), Is.EqualTo(AdminSecretTestEnvironment.Secret));
        Assert.That(factory.RequestedNames, Is.All.EqualTo(HttpClientNames.Matchmaking));
    }

    [Test]
    public async Task ReadRoutes_SendNoBody([Values(0, 1, 2, 9, 10)] int index)
    {
        var (repository, handler, _) = Create();

        await Invoke(repository, index);

        Assert.That(handler.RequestBodies.Single(), Is.Empty);
    }

    [Test]
    public async Task List_ForwardsOnlyGivenFilters_Encoded()
    {
        var (repository, handler, _) = Create();

        await repository.GetSmurfEvents("unreviewed", "manual", "a+b/c=", 50);
        await repository.GetSmurfEvents(null, null, null, null);

        Assert.That(handler.Requests[0].RequestUri!.Query, Is.EqualTo("?review=unreviewed&origin=manual&cursor=a%2Bb%2Fc%3D&limit=50"));
        Assert.That(handler.Requests[1].RequestUri!.Query, Is.Empty);
    }

    [Test]
    public async Task IdSegment_IsEncoded()
    {
        var (repository, handler, _) = Create();

        await repository.GetSmurfEvent("a/b");

        Assert.That(PathOf(handler.Requests.Single()), Is.EqualTo(Base + "/a%2Fb"));
    }

    [Test]
    public async Task Create_And_Update_SendEveryFieldAndTheAuthor([Values(4, 5)] int index)
    {
        var (repository, handler, _) = Create();

        await Invoke(repository, index);

        var body = JObject.Parse(handler.RequestBodies.Single());
        Assert.That(body["name"]!.Value<string>(), Is.EqualTo("Name"));
        Assert.That(body["from"]!.Value<string>(), Is.EqualTo("2026-01-01"));
        Assert.That(body["to"]!.Value<string>(), Is.EqualTo("2026-01-02"));
        Assert.That(body["anchors"]!.Values<string>(), Is.EqualTo(Anchors));
        Assert.That(body["reason"]!.Value<string>(), Is.EqualTo("why"));
        Assert.That(body["author"]!.Value<string>(), Is.EqualTo("Mod#1"));
    }

    [Test]
    public async Task Preview_SendsNeitherAuthorNorReason()
    {
        var (repository, handler, _) = Create();

        await Invoke(repository, 3);

        var body = JObject.Parse(handler.RequestBodies.Single());
        Assert.That(body.Properties().Select(p => p.Name), Is.EquivalentTo(new[] { "name", "from", "to", "anchors" }));
    }

    [Test]
    public async Task AbsentReason_IsOmittedNotNull()
    {
        var (repository, handler, _) = Create();

        await repository.DeleteSmurfEvent("ev-1", null, "Mod#1");
        await repository.ReviewSmurfEvent("ev-1", "denied", null, null, "Mod#1");
        await repository.CreateSmurfEvent("Name", "a", "b", Anchors, null, "Mod#1");

        foreach (var raw in handler.RequestBodies)
            Assert.That(JObject.Parse(raw).ContainsKey("reason"), Is.False);
    }

    [Test]
    public async Task Delete_And_Review_SendAuthor_AndReviewSendsDecision()
    {
        var (repository, handler, _) = Create();

        await Invoke(repository, 6);
        await Invoke(repository, 7);

        var delete = JObject.Parse(handler.RequestBodies[0]);
        var review = JObject.Parse(handler.RequestBodies[1]);
        Assert.That(delete["author"]!.Value<string>(), Is.EqualTo("Mod#1"));
        Assert.That(review["author"]!.Value<string>(), Is.EqualTo("Mod#1"));
        Assert.That(review["decision"]!.Value<string>(), Is.EqualTo("confirmed"));
    }

    [Test]
    public async Task Rename_SendsNameAndAuthor_AndOmitsAnAbsentReason()
    {
        var (repository, handler, _) = Create();

        await repository.RenameSmurfEvent("ev-1", "Sofia LAN", null, "Mod#1");
        await repository.RenameSmurfEvent("ev-1", "Sofia LAN", "typo", "Mod#1");

        Assert.That(handler.RequestBodies[0], Is.EqualTo("{\"name\":\"Sofia LAN\",\"author\":\"Mod#1\"}"));
        Assert.That(JObject.Parse(handler.RequestBodies[1])["reason"]!.Value<string>(), Is.EqualTo("typo"));
    }

    [Test]
    public async Task Identifiers_SendsRevealOnlyWhenTrue()
    {
        var (repository, handler, _) = Create();

        await repository.GetSmurfEventIdentifiers("ev-1", true);
        await repository.GetSmurfEventIdentifiers("ev-1", false);

        Assert.That(handler.Requests[0].RequestUri!.Query, Is.EqualTo("?reveal=true"));
        Assert.That(handler.Requests[1].RequestUri!.Query, Is.Empty);
    }

    [Test]
    public async Task Review_SendsTheNameOnlyWhenGiven()
    {
        var (repository, handler, _) = Create();

        await repository.ReviewSmurfEvent("ev-1", "confirmed", null, "Sofia LAN", "Mod#1");
        await repository.ReviewSmurfEvent("ev-1", "confirmed", null, null, "Mod#1");

        Assert.That(JObject.Parse(handler.RequestBodies[0])["name"]!.Value<string>(), Is.EqualTo("Sofia LAN"));
        Assert.That(JObject.Parse(handler.RequestBodies[1]).ContainsKey("name"), Is.False);
    }

    [TestCase(HttpStatusCode.OK, "{\"events\":[]}")]
    [TestCase(HttpStatusCode.Created, "{\"id\":\"ev-1\"}")]
    [TestCase(HttpStatusCode.NoContent, "")]
    [TestCase(HttpStatusCode.BadRequest, "{\"error\":\"bad\"}")]
    [TestCase(HttpStatusCode.NotFound, "{\"error\":\"nope\"}")]
    [TestCase(HttpStatusCode.Conflict, "{\"error\":\"conflict\"}")]
    [TestCase(HttpStatusCode.UnprocessableEntity, "{\"error\":\"refused\",\"preview\":{\"refused\":\"no-venue\"}}")]
    public async Task MatchmakingStatusAndBody_PassThroughUnchanged(HttpStatusCode status, string body)
    {
        var (repository, _, _) = Create(status, body);

        var result = await repository.GetSmurfEvent("ev-1");

        Assert.That(result.StatusCode, Is.EqualTo((int)status));
        Assert.That(result.Body, Is.EqualTo(body));
    }

    // The status and message body are defined by matchmaking and must reach the caller untouched.
    [Test]
    public async Task ServiceUnavailable_FromWriteRoutes_ReachesTheCallerWithItsMessage([Range(3, 5)] int index)
    {
        const string body = "{\"error\":\"placeholder retry message\"}";
        var (repository, _, _) = Create(HttpStatusCode.ServiceUnavailable, body);

        var result = await Invoke(repository, index);

        Assert.That(result.StatusCode, Is.EqualTo(503));
        Assert.That(result.Body, Is.EqualTo(body));
    }

    // Matchmaking answers 429 with its own short body when busy; only 403 and network failures are rewritten.
    [Test]
    public async Task Busy429_FromPreviewCreateAndUpdate_ReachesTheCallerWithItsBody([Range(3, 5)] int index)
    {
        const string body = "{\"error\":\"placeholder busy message\"}";
        var (repository, _, _) = Create(HttpStatusCode.TooManyRequests, body);

        var result = await Invoke(repository, index);

        Assert.That(result.StatusCode, Is.EqualTo(429));
        Assert.That(result.Body, Is.EqualTo(body));
    }

    [Test]
    public async Task Preview_SendsTheEventItReplacesOnlyWhenGiven()
    {
        var (repository, handler, _) = Create();

        await repository.PreviewSmurfEvent("Name", "2026-01-01", "2026-01-02", Anchors, "ev-1");
        await repository.PreviewSmurfEvent("Name", "2026-01-01", "2026-01-02", Anchors, null);

        var withReplacing = JObject.Parse(handler.RequestBodies[0]);
        var without = JObject.Parse(handler.RequestBodies[1]);
        Assert.That(withReplacing["replacing"]!.Value<string>(), Is.EqualTo("ev-1"));
        Assert.That(without.ContainsKey("replacing"), Is.False);
    }

    [Test]
    public async Task ConflictOnUpdate_AndBusyOnReview_ReachTheCallerUnchanged()
    {
        const string body = "{\"error\":\"placeholder\"}";
        var (conflictRepository, _, _) = Create(HttpStatusCode.Conflict, body);
        var (busyRepository, _, _) = Create(HttpStatusCode.TooManyRequests, body);

        var put = await Invoke(conflictRepository, 5);
        var review = await Invoke(busyRepository, 7);

        Assert.That((put.StatusCode, put.Body), Is.EqualTo((409, body)));
        Assert.That((review.StatusCode, review.Body), Is.EqualTo((429, body)));
    }

    [Test]
    public async Task InternalServerError_IsPassedThroughUnchanged()
    {
        const string body = "{\"error\":\"placeholder error\"}";
        var (repository, _, _) = Create(HttpStatusCode.InternalServerError, body);

        var result = await repository.GetSmurfEvent("ev-1");

        Assert.That(result.StatusCode, Is.EqualTo(500));
        Assert.That(result.Body, Is.EqualTo(body));
    }

    [Test]
    public async Task Matchmaking403_BecomesGeneric502_WithoutTheUpstreamBody()
    {
        var (repository, _, _) = Create(HttpStatusCode.Forbidden, "{\"error\":\"bad x-admin-secret\"}");

        var result = await repository.GetSmurfEvent("ev-1");

        Assert.That(result.StatusCode, Is.EqualTo(502));
        Assert.That(result.Body, Does.Not.Contain("secret"));
    }
}
