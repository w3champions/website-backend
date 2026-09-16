using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Serilog.Events;
using W3C.Contracts.Matchmaking;
using W3C.Domain.MatchmakingService;
using W3C.Domain.MatchmakingService.Contracts;
using static WC3ChampionsStatisticService.Tests.Maps.TemporaryMapClientTests;

namespace WC3ChampionsStatisticService.Tests.Maps;

/// <summary>
/// A body that breaks the Appendix A.5/A.7 contract on an otherwise expected status (2xx, or 409 on create) is an
/// upstream fault. It must surface as an HttpRequestException carrying that status, never as a JsonException, a
/// null record or an empty page, and its message must quote neither the body nor a URI. The pre-existing
/// update-service map-file reads behind the admin Maps page and matchmaking's two map listings follow the same rule.
/// </summary>
[TestFixture]
public class TemporaryMapClientContractTests
{
    private const string BodyMarker = "proxy.internal.example";

    private static readonly (string Label, string Body)[] UnreadableBodies =
    [
        ("Empty", ""),
        ("Whitespace", " \r\n "),
        ("JsonNull", "null"),
        ("Html", "<html><body>502 from " + BodyMarker + "</body></html>"),
        ("Truncated", "{\"map\":{\"id\":58"),
        ("WrongRootType", "[]"),
        ("TwoObjects", "{}{}"),
    ];

    private sealed record Route(
        string Name,
        HttpMethod Method,
        string Path,
        HttpStatusCode Status,
        Func<ScriptedHttpHandler, Task> Call,
        string[] BodiesWithoutTheRecord,
        bool ReadsAnArray = false);

    private static readonly Route[] Routes =
    [
        new("GetTemporaryMapBySha1", HttpMethod.Get, "/maps/temporary/by-sha1/", HttpStatusCode.OK,
            h => Mm(h).GetTemporaryMapBySha1(Sha1), ["{}", "{\"map\":null}", "{\"map\":\"" + BodyMarker + "\"}"]),
        new("GetTemporaryMapStateByProofHash", HttpMethod.Post, "/maps/temporary/by-proof-hash", HttpStatusCode.OK,
            h => Mm(h).GetTemporaryMapStateByProofHash(ProofHash), ["{}", "{\"fileState\":\"\"}", "{\"fileState\":null}"]),
        new("GetTemporaryMapByPath", HttpMethod.Get, "/maps/temporary/by-path", HttpStatusCode.OK,
            h => Mm(h).GetTemporaryMapByPath(FileKey), ["{}", "{\"map\":null}"]),
        new("CreateTemporaryMap_Created", HttpMethod.Post, "/maps/temporary", HttpStatusCode.Created,
            h => Mm(h).CreateTemporaryMap(SampleCreateRequest()), ["{}", "{\"map\":null}"]),
        new("CreateTemporaryMap_Conflict", HttpMethod.Post, "/maps/temporary", HttpStatusCode.Conflict,
            h => Mm(h).CreateTemporaryMap(SampleCreateRequest()), ["{}", "{\"map\":null}"]),
        new("VerifyTemporaryMapProof", HttpMethod.Post, "/maps/temporary/verify-proof", HttpStatusCode.OK,
            h => Mm(h).VerifyTemporaryMapProof(ProofHash), ["{}", "{\"mapId\":5811,\"path\":\"" + FileKey + "\"}"]),
        new("MarkTemporaryMapFileRestored", HttpMethod.Post, "/file-restored", HttpStatusCode.OK,
            h => Mm(h).MarkTemporaryMapFileRestored(5811,
                new TemporaryMapFileRestoredRequest { Sha1 = Sha1, Uploader = "peter#123", MapProof = MapProof }),
            ["{}", "{\"map\":null}"]),
        new("MarkTemporaryMapFileDeleted", HttpMethod.Post, "/file-deleted", HttpStatusCode.OK,
            h => Mm(h).MarkTemporaryMapFileDeleted(5811), ["{}", "{\"map\":null}"]),
        // A null row is no record and no file, so a page holding one breaks the contract like a page without its array:
        // the expiry sweep iterates both listings per row.
        new("GetExpiredTemporaryMaps", HttpMethod.Get, "/maps/temporary/expired", HttpStatusCode.OK,
            h => Mm(h).GetExpiredTemporaryMaps(1, 200),
            ["{}", "{\"items\":null}", "{\"items\":{}}", "{\"items\":[null]}", "{\"items\":[{\"id\":1,\"path\":\"" + FileKey + "\"},null]}"]),
        new("UploadTemporaryMapAsync", HttpMethod.Post, "/api/content/maps", HttpStatusCode.OK,
            UploadAbc, []),
        new("ListMapFilesAsync", HttpMethod.Get, "/api/content/maps/files", HttpStatusCode.OK,
            h => Us(h).ListMapFilesAsync("W3Champions/CustomGames/", 24, null, 500, CancellationToken.None),
            ["{}", "{\"files\":null}", "{\"next\":\"W3Champions/CustomGames/z.w3x\"}",
             "{\"files\":[null],\"next\":null}", "{\"files\":[{\"filePath\":\"" + FileKey + "\"},null],\"next\":null}"]),
        new("GetMapFiles", HttpMethod.Get, "/api/content/maps?mapId=7", HttpStatusCode.OK,
            h => Us(h).GetMapFiles(7), [], ReadsAnArray: true),
        new("GetMapFile", HttpMethod.Get, "/api/content/maps/f1", HttpStatusCode.OK,
            h => Us(h).GetMapFile("f1"), []),
        new("CreateMapFromFormAsync", HttpMethod.Post, "/api/content/maps", HttpStatusCode.OK,
            h => Us(h).CreateMapFromFormAsync(new HttpRequestMessage { Content = new StringContent("form") }, "Admin#1"), []),
        // A null row is no map, so a listing holding one breaks the contract like a listing without its items.
        new("GetMaps", HttpMethod.Get, "/maps?filter=x", HttpStatusCode.OK,
            h => Mm(h).GetMaps(new GetMapsRequest { Filter = "x" }),
            ["{}", "{\"total\":0}", "{\"total\":0,\"items\":null}", "{\"total\":0,\"items\":{}}",
             "{\"total\":1,\"items\":[null]}", "{\"total\":2,\"items\":[{\"id\":7,\"name\":\"Echo Isles\"},null]}"]),
        new("GetTournamentMaps", HttpMethod.Get, "/maps/tournaments", HttpStatusCode.OK,
            h => Mm(h).GetTournamentMaps(),
            ["{}", "{\"total\":0}", "{\"total\":0,\"items\":null}", "{\"total\":0,\"items\":{}}",
             "{\"total\":1,\"items\":[null]}", "{\"total\":2,\"items\":[{\"id\":7,\"name\":\"Echo Isles\"},null]}"]),
    ];

    private static IEnumerable<TestCaseData> ContractViolations()
    {
        foreach (var route in Routes)
        {
            foreach (var (label, body) in UnreadableBodies)
            {
                // An array is the right root type for an array read, so an object stands in for it there.
                var wrongRoot = label == "WrongRootType" && route.ReadsAnArray ? "{}" : body;
                yield return new TestCaseData(route.Name, wrongRoot).SetName($"{route.Name}_{(int)route.Status}Body{label}");
            }

            for (var i = 0; i < route.BodiesWithoutTheRecord.Length; i++)
            {
                yield return new TestCaseData(route.Name, route.BodiesWithoutTheRecord[i])
                    .SetName($"{route.Name}_{(int)route.Status}BodyWithoutTheRecord{i + 1}");
            }
        }
    }

    [TestCaseSource(nameof(ContractViolations))]
    public void ContractViolatingBody_ThrowsWithTheResponseStatus_AndQuotesNeitherBodyNorUri(string routeName, string body)
    {
        var route = Routes.Single(r => r.Name == routeName);
        var handler = new ScriptedHttpHandler().On(route.Method, route.Path, route.Status, body);

        var ex = Assert.ThrowsAsync<HttpRequestException>(() => route.Call(handler));

        Assert.That(handler.Requests, Has.Count.EqualTo(1));
        Assert.That(ex!.StatusCode, Is.EqualTo(route.Status));
        Assert.That(ex.Message, Does.Not.Contain(BodyMarker).And.Not.Contain("://").And.Not.Contain("/maps").And.Not.Contain("/api"));
        Assert.That(ex.InnerException, Is.Null, "a parser exception can quote the body");
    }

    [TestCase("GetMaps", HttpStatusCode.InternalServerError, "{\"message\":\"failed behind " + BodyMarker + "\"}")]
    [TestCase("GetMaps", HttpStatusCode.BadGateway, "<html><body>502 from " + BodyMarker + "</body></html>")]
    [TestCase("GetMaps", HttpStatusCode.NotFound, "{}")]
    [TestCase("GetTournamentMaps", HttpStatusCode.InternalServerError, "{\"message\":\"failed behind " + BodyMarker + "\"}")]
    [TestCase("GetTournamentMaps", HttpStatusCode.BadGateway, "<html><body>502 from " + BodyMarker + "</body></html>")]
    [TestCase("GetTournamentMaps", HttpStatusCode.NotFound, "{}")]
    public void MapListing_OnAnErrorStatus_ThrowsWithThatStatus_AndQuotesNeitherBodyNorUri(string listing, HttpStatusCode status, string body)
    {
        // Before, any answer was read as a listing, so a matchmaking error became an empty 200 page.
        var route = Routes.Single(r => r.Name == listing);
        var handler = new ScriptedHttpHandler().On(route.Method, route.Path, status, body);

        var ex = Assert.ThrowsAsync<HttpRequestException>(() => route.Call(handler));

        Assert.That(ex!.StatusCode, Is.EqualTo(status));
        Assert.That(ex.Message, Does.Not.Contain(BodyMarker).And.Not.Contain("://").And.Not.Contain("/maps").And.Not.Contain("{"));
        Assert.That(ex.InnerException, Is.Null);
    }

    [TestCase("GetMaps", HttpStatusCode.Unauthorized)]
    [TestCase("GetMaps", HttpStatusCode.Forbidden)]
    [TestCase("GetMaps", HttpStatusCode.ProxyAuthenticationRequired)]
    [TestCase("GetTournamentMaps", HttpStatusCode.Unauthorized)]
    [TestCase("GetTournamentMaps", HttpStatusCode.Forbidden)]
    [TestCase("GetTournamentMaps", HttpStatusCode.ProxyAuthenticationRequired)]
    public void MapListing_WhenMatchmakingRefusesWebsiteBackend_ThrowsBadGateway_NamingTheUpstreamStatus(string listing, HttpStatusCode upstreamStatus)
    {
        // A 401 or 403 from matchmaking means website-backend's own admin-secret configuration is wrong, and a 407 that
        // a proxy on the way demands credentials: never the caller's authentication. Relayed as-is, the website would
        // treat it as the caller's own auth failure.
        var route = Routes.Single(r => r.Name == listing);
        var handler = new ScriptedHttpHandler().On(route.Method, route.Path, upstreamStatus,
            "{\"message\":\"refused behind " + BodyMarker + "\"}");

        var ex = Assert.ThrowsAsync<HttpRequestException>(() => route.Call(handler));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
        Assert.That(ex.Message, Does.Contain(((int)upstreamStatus).ToString()), "the upstream status is the diagnostic");
        Assert.That(ex.Message, Does.Not.Contain(BodyMarker).And.Not.Contain("://").And.Not.Contain("/maps").And.Not.Contain("{"));
        Assert.That(ex.InnerException, Is.Null);
    }

    [TestCase("GetMaps", HttpStatusCode.Unauthorized)]
    [TestCase("GetMaps", HttpStatusCode.Forbidden)]
    [TestCase("GetMaps", HttpStatusCode.ProxyAuthenticationRequired)]
    [TestCase("GetTournamentMaps", HttpStatusCode.Unauthorized)]
    [TestCase("GetTournamentMaps", HttpStatusCode.Forbidden)]
    [TestCase("GetTournamentMaps", HttpStatusCode.ProxyAuthenticationRequired)]
    public void MapListing_WhenMatchmakingRefusesWebsiteBackend_LogsTheUpstreamStatusAtWarning_NamingNeitherBodyUriNorSecret(
        string listing, HttpStatusCode upstreamStatus)
    {
        // The global filter logs only the 502 it answers, so this is the one log line that records what matchmaking
        // said. It names the service, the listing and the upstream status, and nothing from the request or the body.
        // The response carries its request, as HttpClientHandler's do, so a line that reached for the URL would show it.
        var route = Routes.Single(r => r.Name == listing);
        var body = "{\"message\":\"refused behind " + BodyMarker + "\",\"url\":\"https://" + BodyMarker + "/maps?secret=placeholder\"}";
        var handler = new ScriptedHttpHandler().On(
            r => r.Method == route.Method && r.RequestUri!.PathAndQuery.Contains(route.Path, StringComparison.Ordinal),
            r => { var response = ScriptedHttpHandler.Json(upstreamStatus, body); response.RequestMessage = r; return response; });

        var logEvent = LogEventsWhile(() => route.Call(handler)).Single();

        Assert.That(logEvent.Level, Is.EqualTo(LogEventLevel.Warning));
        Assert.That(logEvent.Exception, Is.Null);
        Assert.That(Scalar(logEvent, "Service"), Is.EqualTo("matchmaking-service"));
        Assert.That(Scalar(logEvent, "Listing"), Is.EqualTo(listing));
        Assert.That(Scalar(logEvent, "UpstreamStatusCode"), Is.EqualTo((int)upstreamStatus));
        var logged = logEvent.RenderMessage() + string.Join(",", logEvent.Properties.Values);
        Assert.That(logged, Does.Contain(((int)upstreamStatus).ToString()));
        Assert.That(logged, Does.Not.Contain(BodyMarker).And.Not.Contain("://").And.Not.Contain("/maps").And.Not.Contain("{"));
        Assert.That(ConfiguredAdminSecret, Is.Not.Empty);
        Assert.That(logged, Does.Not.Contain(ConfiguredAdminSecret));
    }

    [TestCase("GetMaps", HttpStatusCode.InternalServerError)]
    [TestCase("GetTournamentMaps", HttpStatusCode.NotFound)]
    public void MapListing_OnAnyOtherErrorStatus_LeavesTheLoggingToTheGlobalFilter(string listing, HttpStatusCode status)
    {
        // The filter logs these with their real status, so a client line would only duplicate it.
        var route = Routes.Single(r => r.Name == listing);
        var handler = new ScriptedHttpHandler().On(route.Method, route.Path, status, "{}");

        var logEvents = LogEventsWhile(() => route.Call(handler));

        Assert.That(logEvents, Is.Empty);
    }

    [Test]
    public async Task MapListings_StillReadWellFormedBodies()
    {
        const string listing = "{\"total\":1,\"items\":[{\"id\":7,\"name\":\"Echo Isles\"}]}";
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Get, "/maps/tournaments", HttpStatusCode.OK, listing)
            .On(HttpMethod.Get, "/maps?filter=x", HttpStatusCode.OK, "{\"total\":0,\"items\":[]}");
        var client = Mm(handler);

        var tournaments = await client.GetTournamentMaps();
        var maps = await client.GetMaps(new GetMapsRequest { Filter = "x" });

        Assert.That(tournaments.Total, Is.EqualTo(1));
        Assert.That(tournaments.Items.Single().Name, Is.EqualTo("Echo Isles"));
        Assert.That(maps.Items, Is.Empty);
    }

    [Test]
    public async Task UpdateServiceMapFileReads_StillReadWellFormedBodies()
    {
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Get, "/api/content/maps?mapId=7", HttpStatusCode.OK, "[]")
            .On(HttpMethod.Get, "/api/content/maps/f1", HttpStatusCode.OK, "{\"id\":\"f1\",\"mapId\":7}")
            .On(HttpMethod.Post, "/api/content/maps", HttpStatusCode.OK, "{\"id\":\"f2\",\"mapId\":7}");
        var client = Us(handler);

        var files = await client.GetMapFiles(7);
        var file = await client.GetMapFile("f1");
        var created = await client.CreateMapFromFormAsync(new HttpRequestMessage { Content = new StringContent("form") }, "Admin#1");

        Assert.That(files, Is.Empty);
        Assert.That(file.Id, Is.EqualTo("f1"));
        Assert.That(created.Id, Is.EqualTo("f2"));
    }

    [Test]
    public async Task GetExpiredTemporaryMaps_ReadsAnEmptyPageAsEmpty()
    {
        var handler = new ScriptedHttpHandler().On(HttpMethod.Get, "/maps/temporary/expired", HttpStatusCode.OK, "{\"items\":[]}");

        var expired = await Mm(handler).GetExpiredTemporaryMaps(1, 200);

        Assert.That(expired.Items, Is.Empty);
    }

    [TestCase("{\"files\":[],\"next\":\"W3Champions/CustomGames/z.w3x\"}", "W3Champions/CustomGames/z.w3x")]
    [TestCase("{\"files\":[],\"next\":null}", null)]
    [TestCase("{\"files\":[]}", null)]
    public async Task ListMapFilesAsync_ReadsTheNextCursor(string body, string expectedNext)
    {
        var handler = new ScriptedHttpHandler().On(HttpMethod.Get, "/api/content/maps/files", HttpStatusCode.OK, body);

        var listing = await Us(handler).ListMapFilesAsync("W3Champions/CustomGames/", 24, null, 500, CancellationToken.None);

        Assert.That(listing.Files, Is.Empty);
        Assert.That(listing.Next, Is.EqualTo(expectedNext));
    }

    [Test]
    public void GetExpiredTemporaryMaps_ForwardsTheCancellationToken()
    {
        var handler = new ScriptedHttpHandler().On(HttpMethod.Get, "/maps/temporary/expired", HttpStatusCode.OK, "{\"items\":[]}");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.CatchAsync<OperationCanceledException>(() => Mm(handler).GetExpiredTemporaryMaps(1, 200, cancelled.Token));
    }

    private static async Task UploadAbc(ScriptedHttpHandler handler)
    {
        await using var bytes = new MemoryStream(Encoding.UTF8.GetBytes("abc"));
        await Us(handler).UploadTemporaryMapAsync(bytes, "CustomGames/x-94ec3bda.w3x", 0, "peter#123", CancellationToken.None);
    }

    /// <summary>The secret the client sends, read from its field so no test carries a copy of the value.</summary>
    private static readonly string ConfiguredAdminSecret = (string)typeof(MatchmakingServiceClient)
        .GetField("AdminSecret", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

    /// <summary>The Serilog events a call that must throw an HttpRequestException writes to the static logger.</summary>
    private static IReadOnlyList<LogEvent> LogEventsWhile(AsyncTestDelegate call)
    {
        var sink = new CapturingLogSink();
        using (sink.CaptureStaticLogger())
        {
            Assert.ThrowsAsync<HttpRequestException>(call);
        }

        return sink.Events;
    }

    private static object Scalar(LogEvent logEvent, string property)
        => ((ScalarValue)logEvent.Properties[property]).Value;
}
