using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using W3C.Domain.MatchmakingService;

namespace WC3ChampionsStatisticService.Tests.Admin;

[TestFixture]
public class MatchmakingServiceClientCommercialLicenseTests
{
    private const string TaggedPlayerJson =
        "{\"battleTag\":\"Grubby#1234\",\"note\":\"Streams for money\",\"notify\":true,\"createdBy\":\"Admin#1\",\"createdAt\":\"2026-10-08T10:00:00.000Z\",\"updatedBy\":\"Admin#2\",\"updatedAt\":\"2026-10-08T11:00:00.000Z\"}";

    // Mirrors MatchmakingServiceClient.AdminSecret (private static): env var with the same default.
    private static readonly string ExpectedAdminSecret =
        Environment.GetEnvironmentVariable("ADMIN_SECRET") ?? "300C018C-6321-4BAB-B289-9CB3DB760CBB";

    private static MatchmakingServiceClient CreateClient(StubMatchmakingHandler handler) =>
        new(new StubHttpClientFactory(new HttpClient(handler)));

    [Test]
    public async Task GetTaggedPlayersParsesDtosAndSendsAdminSecret()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, $"[{TaggedPlayerJson}]");

        var players = await CreateClient(handler).GetCommercialLicenseTaggedPlayers();

        Assert.That(handler.Requests, Has.Count.EqualTo(1));
        Assert.That(handler.Requests[0].Method, Is.EqualTo(HttpMethod.Get));
        Assert.That(handler.Requests[0].Headers.GetValues("x-admin-secret"), Is.EqualTo(new[] { ExpectedAdminSecret }));
        Assert.That(handler.Requests[0].RequestUri!.AbsolutePath, Does.EndWith("/admin/commercial-license/tagged-players"));
        Assert.That(players, Has.Count.EqualTo(1));
        Assert.That(players[0].battleTag, Is.EqualTo("Grubby#1234"));
        Assert.That(players[0].note, Is.EqualTo("Streams for money"));
        Assert.That(players[0].notify, Is.True);
        Assert.That(players[0].createdBy, Is.EqualTo("Admin#1"));
        Assert.That(players[0].updatedBy, Is.EqualTo("Admin#2"));
        Assert.That(players[0].createdAt, Is.EqualTo(new DateTime(2026, 10, 8, 10, 0, 0, DateTimeKind.Utc)));
        Assert.That(players[0].updatedAt, Is.EqualTo(new DateTime(2026, 10, 8, 11, 0, 0, DateTimeKind.Utc)));
    }

    [Test]
    public async Task GetTaggedPlayersReturnsEmptyListForEmptyBody()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, "");

        var players = await CreateClient(handler).GetCommercialLicenseTaggedPlayers();

        Assert.That(players, Is.Empty);
    }

    [Test]
    public async Task UpsertEscapesBattleTagAndSendsCamelCaseBody()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.OK, TaggedPlayerJson);

        var result = await CreateClient(handler).UpsertCommercialLicenseTaggedPlayer("Grubby#1234", new CommercialLicenseTaggedPlayerRequest
        {
            note = "Streams for money",
            notify = true,
            actingBattleTag = "Admin#1",
        });

        Assert.That(handler.Requests, Has.Count.EqualTo(1));
        Assert.That(handler.Requests[0].Method, Is.EqualTo(HttpMethod.Put));
        Assert.That(handler.Requests[0].Headers.GetValues("x-admin-secret"), Is.EqualTo(new[] { ExpectedAdminSecret }));
        Assert.That(handler.Requests[0].RequestUri!.AbsolutePath, Does.EndWith("/admin/commercial-license/tagged-players/Grubby%231234"));

        var body = JObject.Parse(handler.RequestBodies[0]);
        Assert.That(body["note"]!.Value<string>(), Is.EqualTo("Streams for money"));
        Assert.That(body["notify"]!.Value<bool>(), Is.True);
        Assert.That(body["actingBattleTag"]!.Value<string>(), Is.EqualTo("Admin#1"));

        Assert.That(result.battleTag, Is.EqualTo("Grubby#1234"));
    }

    [Test]
    public void UpsertSurfacesMatchmakingValidationErrorMessage()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.BadRequest, "{\"error\":\"note must be at most 500 characters\"}");

        var ex = Assert.ThrowsAsync<HttpRequestException>(async () =>
            await CreateClient(handler).UpsertCommercialLicenseTaggedPlayer("Grubby#1234", new CommercialLicenseTaggedPlayerRequest
            {
                note = new string('x', 501),
                notify = false,
                actingBattleTag = "Admin#1",
            }));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(ex.Message, Is.EqualTo("note must be at most 500 characters"));
    }

    [Test]
    public async Task DeleteEscapesBattleTagAndAcceptsNoContent()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.NoContent);

        await CreateClient(handler).DeleteCommercialLicenseTaggedPlayer("Grubby#1234");

        Assert.That(handler.Requests, Has.Count.EqualTo(1));
        Assert.That(handler.Requests[0].Method, Is.EqualTo(HttpMethod.Delete));
        Assert.That(handler.Requests[0].Headers.GetValues("x-admin-secret"), Is.EqualTo(new[] { ExpectedAdminSecret }));
        Assert.That(handler.Requests[0].RequestUri!.AbsolutePath, Does.EndWith("/admin/commercial-license/tagged-players/Grubby%231234"));
    }

    [Test]
    public void DeleteOfUntaggedPlayerThrowsNotFoundEvenWithEmptyBody()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.NotFound);

        var ex = Assert.ThrowsAsync<HttpRequestException>(async () =>
            await CreateClient(handler).DeleteCommercialLicenseTaggedPlayer("Nobody#1"));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [TestCase(HttpStatusCode.BadGateway, "<html>502 Bad Gateway</html>")]
    [TestCase(HttpStatusCode.InternalServerError, "plain text failure")]
    [TestCase(HttpStatusCode.BadRequest, "{\"error\":{\"code\":1}}")]
    [TestCase(HttpStatusCode.BadRequest, "[1,2]")]
    public void NonStandardErrorBodiesStillThrowHttpRequestExceptionWithStatusCode(HttpStatusCode status, string body)
    {
        var handler = new StubMatchmakingHandler(status, body);

        var ex = Assert.ThrowsAsync<HttpRequestException>(async () =>
            await CreateClient(handler).DeleteCommercialLicenseTaggedPlayer("Grubby#1234"));

        Assert.That(ex!.StatusCode, Is.EqualTo(status));
    }

    [Test]
    public void LegacyErrorsArrayShapeIsStillFormattedAsParamAndMessage()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.BadRequest, "{\"errors\":[{\"msg\":\"m\",\"param\":\"p\"}]}");

        var ex = Assert.ThrowsAsync<HttpRequestException>(async () =>
            await CreateClient(handler).GetCommercialLicenseTaggedPlayers());

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(ex.Message, Is.EqualTo("p m"));
    }

    [Test]
    public void ErrorBodyWithNeitherErrorsNorErrorStillThrowsWithStatusCode()
    {
        var handler = new StubMatchmakingHandler(HttpStatusCode.Forbidden, "{}");

        var ex = Assert.ThrowsAsync<HttpRequestException>(async () =>
            await CreateClient(handler).GetCommercialLicenseTaggedPlayers());

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }
}
