using System.Linq;
using System.Net;
using System.Threading.Tasks;
using MongoDB.Driver;
using NUnit.Framework;
using W3C.Domain;
using W3ChampionsStatisticService.Admin;
using W3ChampionsStatisticService.Admin.Logs;

namespace WC3ChampionsStatisticService.Tests.Admin;

[TestFixture]
public class AdminRepositoryHttpTests
{
    // MongoClient connects lazily; these tests never touch the database.
    private static readonly MongoClient UnusedMongo = new("mongodb://localhost:1");

    [Test]
    public async Task AdminRepository_UsesNamedMatchmakingClient_AndSendsTheSecretOncePerRequest()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, "{\"possibleIdentifierTypes\":[\"ipAddress\"]}");
        var factory = new RecordingHttpClientFactory(handler);
        var repository = new AdminRepository(UnusedMongo, factory);

        await repository.GetPossibleIdentifierTypes();
        await repository.GetPossibleIdentifierTypes();

        Assert.That(factory.RequestedNames, Is.All.EqualTo(HttpClientNames.Matchmaking));
        Assert.That(handler.Requests, Has.Count.EqualTo(2));
        foreach (var request in handler.Requests)
        {
            Assert.That(request.Headers.GetValues(AdminSecretProvider.HeaderName).Single(), Is.EqualTo(AdminSecretTestEnvironment.Secret));
        }
    }

    [Test]
    public async Task AdminRepository_DeleteChatBan_SendsSecretOnTheRequest()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.NoContent);
        var repository = new AdminRepository(UnusedMongo, new RecordingHttpClientFactory(handler));

        var status = await repository.DeleteChatBan("ban-1");

        Assert.That(status, Is.EqualTo(HttpStatusCode.NoContent));
        var request = handler.Requests.Single();
        Assert.That(request.Method.Method, Is.EqualTo("DELETE"));
        Assert.That(request.RequestUri!.AbsolutePath, Does.EndWith("/flo/globalChatBans/ban-1"));
        Assert.That(request.Headers.GetValues(AdminSecretProvider.HeaderName).Single(), Is.EqualTo(AdminSecretTestEnvironment.Secret));
    }

    [Test]
    public async Task LogsRepository_UsesNamedLogsClient_AndSendsTheSecretOncePerRequest()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, "[\"a.log\"]");
        var factory = new RecordingHttpClientFactory(handler);
        var repository = new LogsRepository(UnusedMongo, factory);

        var first = await repository.GetLogfileNames();
        await repository.GetLogfileNames();

        Assert.That(first, Is.EqualTo(new[] { "a.log" }));
        Assert.That(factory.RequestedNames, Is.All.EqualTo(HttpClientNames.MatchmakingLogs));
        foreach (var request in handler.Requests)
        {
            Assert.That(request.Headers.GetValues(AdminSecretProvider.HeaderName).Single(), Is.EqualTo(AdminSecretTestEnvironment.Secret));
        }
    }
}
