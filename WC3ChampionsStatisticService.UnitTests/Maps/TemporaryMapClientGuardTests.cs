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
using Microsoft.AspNetCore.WebUtilities;
using NUnit.Framework;
using W3C.Contracts.Matchmaking;
using W3C.Domain.MatchmakingService;
using W3C.Domain.MatchmakingService.Contracts;
using W3C.Domain.UpdateService;
using static WC3ChampionsStatisticService.Tests.Maps.TemporaryMapClientTests;

namespace WC3ChampionsStatisticService.Tests.Maps;

/// <summary>
/// Guards the temporary-map client methods share: the admin secret on every route, strict
/// record-not-found parsing, single URL encoding, and the upload's own long-timeout client.
/// </summary>
[TestFixture]
public class TemporaryMapClientGuardTests
{
    /// <summary>A file key using every character spec §6.4 keeps that is meaningful inside a URL.</summary>
    private const string HostileFileKey = "W3Champions/CustomGames/a%2F..%2Fb&c=d+e #f?g-94ec3bda.w3x";

    private static readonly Dictionary<string, (Type Client, Func<ScriptedHttpHandler, Task> Call)> AdminSecretRoutes = new()
    {
        ["GetTemporaryMapBySha1"] = (typeof(MatchmakingServiceClient), h => Mm(h).GetTemporaryMapBySha1(Sha1)),
        ["GetTemporaryMapStateByProofHash"] = (typeof(MatchmakingServiceClient), h => Mm(h).GetTemporaryMapStateByProofHash(ProofHash)),
        ["GetTemporaryMapByPath"] = (typeof(MatchmakingServiceClient), h => Mm(h).GetTemporaryMapByPath(FileKey)),
        ["CreateTemporaryMap"] = (typeof(MatchmakingServiceClient), h => Mm(h).CreateTemporaryMap(SampleCreateRequest())),
        ["VerifyTemporaryMapProof"] = (typeof(MatchmakingServiceClient), h => Mm(h).VerifyTemporaryMapProof(ProofHash)),
        ["MarkTemporaryMapFileRestored"] = (typeof(MatchmakingServiceClient), h => Mm(h).MarkTemporaryMapFileRestored(5811,
            new TemporaryMapFileRestoredRequest { Sha1 = Sha1, Uploader = "peter#123", MapProof = MapProof })),
        ["MarkTemporaryMapFileDeleted"] = (typeof(MatchmakingServiceClient), h => Mm(h).MarkTemporaryMapFileDeleted(5811)),
        ["GetExpiredTemporaryMaps"] = (typeof(MatchmakingServiceClient), h => Mm(h).GetExpiredTemporaryMaps(1, 200)),
        ["GetMaps_IncludeTemporary"] = (typeof(MatchmakingServiceClient), h => Mm(h).GetMaps(new GetMapsRequest { IncludeTemporary = true })),
        ["GetMaps_PermanentOnly"] = (typeof(MatchmakingServiceClient), h => Mm(h).GetMaps(new GetMapsRequest { Filter = "x" })),
        ["UploadTemporaryMapAsync"] = (typeof(UpdateServiceClient), h => UploadAbc(Us(h))),
        ["DeleteMapFileByPathAsync"] = (typeof(UpdateServiceClient), h => Us(h).DeleteMapFileByPathAsync(FileKey, CancellationToken.None)),
        ["ListMapFilesAsync"] = (typeof(UpdateServiceClient), h => Us(h).ListMapFilesAsync("W3Champions/CustomGames/", 24, null, 500, CancellationToken.None)),
        ["CreateMapFromFormAsync"] = (typeof(UpdateServiceClient), h => Us(h).CreateMapFromFormAsync(
            new HttpRequestMessage { Content = new StringContent("form") }, "Admin#1")),
    };

    private static IEnumerable<string> AdminSecretRouteNames() => AdminSecretRoutes.Keys;

    [TestCaseSource(nameof(AdminSecretRouteNames))]
    public async Task EveryRoute_SendsTheConfiguredAdminSecretOnlyToTheConfiguredService(string route)
    {
        var (clientType, call) = AdminSecretRoutes[route];
        var handler = AllRoutesHandler();

        await call(handler);

        Assert.That(handler.Requests, Has.Count.EqualTo(1));
        var request = handler.Requests[0];
        Assert.That(request.Headers.TryGetValues("x-admin-secret", out var values), Is.True, "x-admin-secret header missing");
        // Compared as a boolean so a failure never prints the secret.
        Assert.That(values!.SequenceEqual([ConfiguredAdminSecret(clientType)]), Is.True,
            "x-admin-secret must carry exactly the client's configured secret");
        Assert.That(request.RequestUri!.GetLeftPart(UriPartial.Authority),
            Is.EqualTo(new Uri(ConfiguredBaseUrl(clientType)).GetLeftPart(UriPartial.Authority)),
            "x-admin-secret may only travel to the client's configured base URL");
    }

    private static IEnumerable<TestCaseData> NullOnNotFoundMethods()
    {
        yield return Probe("GetTemporaryMapBySha1", "/maps/temporary/by-sha1/", c => c.GetTemporaryMapBySha1(Sha1));
        yield return Probe("GetTemporaryMapStateByProofHash", "/maps/temporary/by-proof-hash/", c => c.GetTemporaryMapStateByProofHash(ProofHash));
        yield return Probe("GetTemporaryMapByPath", "/maps/temporary/by-path", c => c.GetTemporaryMapByPath(FileKey));
        yield return Probe("VerifyTemporaryMapProof", "/maps/temporary/verify-proof", c => c.VerifyTemporaryMapProof(ProofHash));
    }

    private static IEnumerable<TestCaseData> NotFoundAsNullCases()
        => from probe in NullOnNotFoundMethods()
           from shape in new[] { (Label: "EmptyObject", Body: "{}"), (Label: "EmptyObjectInWhitespace", Body: " \r\n\t{ \n\t }\r\n ") }
           select new TestCaseData(probe.Arguments[0], probe.Arguments[1], shape.Body).SetName($"{probe.TestName}_404Body{shape.Label}");

    [TestCaseSource(nameof(NotFoundAsNullCases))]
    public async Task RecordNotFound_IsNullOnlyForAnEmptyJsonObject(string path, Func<MatchmakingServiceClient, Task<object>> call, string body)
    {
        var handler = new ScriptedHttpHandler().On(_ => true, _ => ScriptedHttpHandler.Json(HttpStatusCode.NotFound, body));

        Assert.That(await call(Mm(handler)), Is.Null);
        Assert.That(handler.Requests.Single().RequestUri!.PathAndQuery, Does.Contain(path));
    }

    private static IEnumerable<TestCaseData> UnexpectedNotFoundCases()
        => from probe in NullOnNotFoundMethods()
           from shape in new[]
           {
               (Label: "Html", Body: "<!DOCTYPE html><html><body><pre>Cannot GET /maps/temporary</pre></body></html>"),
               (Label: "NonEmptyObject", Body: "{\"message\":\"Not Found\"}"),
               (Label: "Empty", Body: ""),
               (Label: "Array", Body: "[]"),
               (Label: "TwoObjects", Body: "{}{}"),
           }
           select new TestCaseData(probe.Arguments[1], shape.Body).SetName($"{probe.TestName}_404Body{shape.Label}");

    [TestCaseSource(nameof(UnexpectedNotFoundCases))]
    public void RouteLevelNotFound_Throws(Func<MatchmakingServiceClient, Task<object>> call, string body)
    {
        // A proxy page, an Express "Cannot GET" or any other 404 means the route itself was not found, not
        // the record; reading it as "unknown" would create duplicates or delete claimed files (spec §5.4).
        var handler = new ScriptedHttpHandler().On(_ => true, _ => ScriptedHttpHandler.Json(HttpStatusCode.NotFound, body));

        var ex = Assert.ThrowsAsync<HttpRequestException>(() => call(Mm(handler)));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetTemporaryMapByPath_EncodesThePathExactlyOnce()
    {
        var handler = AllRoutesHandler();

        await Mm(handler).GetTemporaryMapByPath(HostileFileKey);

        Assert.That(DecodedQuery(handler.Requests.Single()), Is.EqualTo(new Dictionary<string, string> { ["path"] = HostileFileKey }));
    }

    [Test]
    public async Task DeleteMapFileByPathAsync_EncodesTheFilePathExactlyOnce()
    {
        var handler = AllRoutesHandler();

        await Us(handler).DeleteMapFileByPathAsync(HostileFileKey, CancellationToken.None);

        Assert.That(DecodedQuery(handler.Requests.Single()), Is.EqualTo(new Dictionary<string, string> { ["filePath"] = HostileFileKey }));
    }

    [Test]
    public async Task ListMapFilesAsync_EncodesPrefixAndAfterExactlyOnce()
    {
        var handler = AllRoutesHandler();

        await Us(handler).ListMapFilesAsync(HostileFileKey + "/", 24, HostileFileKey, 500, CancellationToken.None);

        Assert.That(DecodedQuery(handler.Requests.Single()), Is.EqualTo(new Dictionary<string, string>
        {
            ["prefix"] = HostileFileKey + "/",
            ["olderThanHours"] = "24",
            ["limit"] = "500",
            ["after"] = HostileFileKey,
        }));
    }

    [Test]
    public async Task GetMaps_EncodesTheFilterExactlyOnce()
    {
        var handler = AllRoutesHandler();

        await Mm(handler).GetMaps(new GetMapsRequest { Filter = HostileFileKey, IncludeTemporary = true });

        Assert.That(DecodedQuery(handler.Requests.Single()), Is.EqualTo(new Dictionary<string, string>
        {
            ["filter"] = HostileFileKey,
            ["includeTemporary"] = "true",
        }));
    }

    [Test]
    public async Task CreateMapFromFormAsync_EncodesUploadedByExactlyOnce()
    {
        var handler = AllRoutesHandler();
        const string battleTag = "Ad min#1&x=y+z%2F";

        await Us(handler).CreateMapFromFormAsync(new HttpRequestMessage { Content = new StringContent("form") }, battleTag);

        Assert.That(DecodedQuery(handler.Requests.Single()), Is.EqualTo(new Dictionary<string, string> { ["uploadedBy"] = battleTag }));
        Assert.That(handler.Requests.Single().RequestUri!.Query, Is.EqualTo("?uploadedBy=Ad%20min%231%26x%3Dy%2Bz%252F"),
            "RFC 3986 percent-encoding, not form encoding (a space is %20, never +)");
    }

    [Test]
    public async Task CreateMapFromFormAsync_PercentEncodesTheBattleTagHash()
    {
        // A BattleTag's "#" starts a URL fragment if it is sent raw: update-service would see uploadedBy=Player.
        var handler = AllRoutesHandler();

        await Us(handler).CreateMapFromFormAsync(new HttpRequestMessage { Content = new StringContent("form") }, "Player#1234");

        var request = handler.Requests.Single();
        Assert.That(request.RequestUri!.Query, Is.EqualTo("?uploadedBy=Player%231234"));
        Assert.That(request.RequestUri.Fragment, Is.Empty);
    }

    [Test]
    public async Task UploadTemporaryMapAsync_SendsUploadedByAsAFormField_NotOnTheQuery()
    {
        // The temporary-map upload is a form wb builds itself, so the BattleTag travels as a multipart part: no query encoding.
        var handler = AllRoutesHandler();

        await Us(handler).UploadTemporaryMapAsync(new MemoryStream("abc"u8.ToArray()), "CustomGames/x-94ec3bda.w3x", 0, "Player#1234", default);

        var request = handler.Requests.Single();
        Assert.That(request.RequestUri!.Query, Is.Empty);
        Assert.That(handler.RequestBodies.Single(), Does.Match("name=\"?uploadedBy\"?\r\n(?:[^\r\n]+\r\n)*\r\nPlayer#1234\r\n"));
    }

    [Test]
    public async Task CreateMapFromFormAsync_OmitsUploadedByWhenThereIsNoCaller()
    {
        var handler = AllRoutesHandler();

        await Us(handler).CreateMapFromFormAsync(new HttpRequestMessage { Content = new StringContent("form") }, null);

        Assert.That(handler.Requests.Single().RequestUri!.Query, Is.Empty);
    }

    [TestCase("GetMapFile")]
    [TestCase("DeleteMapFile")]
    public async Task MapFileIdRoutes_KeepTheFileIdOneEscapedPathSegment(string method)
    {
        // fileId is a route value of the admin map-file routes; unescaped, its "?" or "#" would rewrite the
        // update-service URL (DeleteMapFile sends x-admin-secret).
        const string hostileFileId = "abc?filePath=W3Champions/v10/x.w3x#f g%2F";
        const string route = "/api/content/maps/";
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Get, route, HttpStatusCode.OK, "{\"id\":\"abc\"}")
            .On(HttpMethod.Delete, route, HttpStatusCode.NoContent, "");
        var client = Us(handler);

        await (method == "GetMapFile" ? client.GetMapFile(hostileFileId) : client.DeleteMapFile(hostileFileId));

        var uri = handler.Requests.Single().RequestUri!;
        Assert.That(uri.Query, Is.Empty, "the file id must not leak into the query");
        var segment = uri.AbsolutePath[(uri.AbsolutePath.IndexOf(route, StringComparison.Ordinal) + route.Length)..];
        Assert.That(segment, Does.Not.Contain("/"), "the file id must stay a single path segment");
        Assert.That(Uri.UnescapeDataString(segment), Is.EqualTo(hostileFileId));
    }

    private static IEnumerable<TestCaseData> MalformedDigestKeys()
    {
        var methods = new (string Name, string Valid, Func<MatchmakingServiceClient, string, Task> Call)[]
        {
            ("GetTemporaryMapBySha1", Sha1, (c, key) => c.GetTemporaryMapBySha1(key)),
            ("GetTemporaryMapStateByProofHash", ProofHash, (c, key) => c.GetTemporaryMapStateByProofHash(key)),
            ("VerifyTemporaryMapProof", ProofHash, (c, key) => c.VerifyTemporaryMapProof(key)),
        };

        foreach (var (name, valid, call) in methods)
        {
            var shapes = new (string Label, string Key)[]
            {
                ("Dot", "."),
                ("DotDot", ".."),
                ("Null", null),
                ("Empty", ""),
                ("Uppercase", valid.ToUpperInvariant()),
                ("OneShort", valid[..^1]),
                ("OneLong", valid + "0"),
                ("NonHex", valid[..^1] + "g"),
                ("OtherDigestLength", valid == Sha1 ? ProofHash : Sha1),
                ("Traversal", "a/../b%2Fc?d#e f"),
            };
            foreach (var (label, key) in shapes)
            {
                yield return new TestCaseData(call, key).SetName($"{name}_Refuses{label}");
            }
        }
    }

    [TestCaseSource(nameof(MalformedDigestKeys))]
    public void DigestKeys_ThatAreNotLowercaseHexOfTheirLength_AreRefusedBeforeAnyRequest(
        Func<MatchmakingServiceClient, string, Task> call, string key)
    {
        // sha1 and proofHash are URL path segments on two of these routes: a "." or ".." key would be collapsed
        // by System.Uri into another route that still carries x-admin-secret.
        var handler = AllRoutesHandler();

        var ex = Assert.ThrowsAsync<ArgumentException>(() => call(Mm(handler), key));

        Assert.That(handler.Requests, Is.Empty);
        if (key?.Length > 8)
        {
            Assert.That(ex!.Message, Does.Not.Contain(key), "a proofHash must never be echoed");
        }
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("W3Champions/v10/EchoIsles.w3x")]
    [TestCase("w3champions/customgames/x-94ec3bda.w3x")]
    [TestCase("/W3Champions/CustomGames/x-94ec3bda.w3x")]
    [TestCase("W3Champions/CustomGames")]
    [TestCase("W3Champions/CustomGames/")]
    [TestCase("W3Champions/CustomGames/..")]
    [TestCase("W3Champions/CustomGames/../v10/EchoIsles.w3x")]
    [TestCase("W3Champions/CustomGames/.")]
    [TestCase("W3Champions/CustomGames/./x-94ec3bda.w3x")]
    [TestCase("W3Champions/CustomGames//x-94ec3bda.w3x")]
    [TestCase("W3Champions/CustomGames/x-94ec3bda.w3x/")]
    [TestCase("W3Champions/CustomGames/a\\..\\..\\v10\\EchoIsles.w3x")]
    public void DeleteMapFileByPathAsync_RefusesAnythingButACustomGamesFileBeforeAnyRequest(string filePath)
    {
        var handler = AllRoutesHandler();

        Assert.ThrowsAsync<ArgumentException>(() => Us(handler).DeleteMapFileByPathAsync(filePath, CancellationToken.None));

        Assert.That(handler.Requests, Is.Empty);
    }

    [TestCase(FileKey)]
    [TestCase(HostileFileKey)]
    [TestCase("W3Champions/CustomGames/a..b...c-94ec3bda.w3m")]
    public async Task DeleteMapFileByPathAsync_AcceptsFileKeysWhoseNamesContainDots(string filePath)
    {
        // §6.4 keeps inner dots and "%2F", so only a whole "." or ".." segment is refused.
        var handler = AllRoutesHandler();

        await Us(handler).DeleteMapFileByPathAsync(filePath, CancellationToken.None);

        Assert.That(DecodedQuery(handler.Requests.Single()), Is.EqualTo(new Dictionary<string, string> { ["filePath"] = filePath }));
    }

    [Test]
    public async Task UploadTemporaryMapAsync_UsesItsOwnClientWithTheLongUploadTimeout()
    {
        var handler = AllRoutesHandler();
        var factory = new ScriptedHttpHandler.Factory(handler);
        var client = new UpdateServiceClient(factory);

        // The shared client has already sent a request, so re-configuring IT would throw.
        await client.DeleteMapFileByPathAsync(FileKey, CancellationToken.None);
        await UploadAbc(client);

        Assert.That(factory.CreatedClients, Has.Count.EqualTo(2), "the upload must not reuse the shared client");
        Assert.That(factory.CreatedClients[0].Timeout, Is.EqualTo(TimeSpan.FromSeconds(100)));
        Assert.That(factory.CreatedClients[1].Timeout, Is.EqualTo(TimeSpan.FromMinutes(10)));
    }

    private static TestCaseData Probe<T>(string name, string path, Func<MatchmakingServiceClient, Task<T>> call)
        where T : class
        => new TestCaseData(path, (Func<MatchmakingServiceClient, Task<object>>)(async c => await call(c))).SetName(name);

    private static async Task UploadAbc(UpdateServiceClient client)
    {
        await using var bytes = new MemoryStream(Encoding.UTF8.GetBytes("abc"));
        await client.UploadTemporaryMapAsync(bytes, "CustomGames/x-94ec3bda.w3x", 0, "peter#123", CancellationToken.None);
    }

    private static string ConfiguredAdminSecret(Type clientType)
        => (string)clientType.GetField("AdminSecret", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);

    private static string ConfiguredBaseUrl(Type clientType)
        => (string)clientType.GetField(
            clientType == typeof(MatchmakingServiceClient) ? "MatchmakingApiUrl" : "UpdateServiceUrl",
            BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);

    private static Dictionary<string, string> DecodedQuery(HttpRequestMessage request)
        => QueryHelpers.ParseQuery(request.RequestUri!.Query).ToDictionary(p => p.Key, p => p.Value.ToString());

    /// <summary>Answers every route of both services with a minimal valid success body.</summary>
    private static ScriptedHttpHandler AllRoutesHandler()
    {
        const string map = "{\"map\":{\"id\":5811,\"path\":\"" + FileKey + "\"}}";
        return new ScriptedHttpHandler()
            .On(HttpMethod.Get, "/maps/temporary/by-sha1/", HttpStatusCode.OK, map)
            .On(HttpMethod.Get, "/maps/temporary/by-proof-hash/", HttpStatusCode.OK, "{\"fileState\":\"present\"}")
            .On(HttpMethod.Get, "/maps/temporary/by-path", HttpStatusCode.OK, map)
            .On(HttpMethod.Get, "/maps/temporary/expired", HttpStatusCode.OK, "{\"items\":[]}")
            .On(HttpMethod.Post, "/maps/temporary/verify-proof", HttpStatusCode.OK,
                "{\"mapId\":5811,\"path\":\"" + FileKey + "\",\"fileState\":\"deleted\"}")
            .On(HttpMethod.Post, "/file-restored", HttpStatusCode.OK, map)
            .On(HttpMethod.Post, "/file-deleted", HttpStatusCode.OK, map)
            .On(HttpMethod.Post, "/maps/temporary", HttpStatusCode.Created, map)
            .On(r => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.EndsWith("/maps", StringComparison.Ordinal),
                _ => ScriptedHttpHandler.Json(HttpStatusCode.OK, "{\"total\":0,\"items\":[]}"))
            .On(HttpMethod.Delete, "/api/content/maps/file", HttpStatusCode.NoContent, "")
            .On(HttpMethod.Get, "/api/content/maps/files", HttpStatusCode.OK, "{\"files\":[],\"next\":null}")
            .On(HttpMethod.Post, "/api/content/maps", HttpStatusCode.OK,
                "{\"id\":\"66f0\",\"mapId\":0,\"filePath\":\"" + FileKey + "\",\"mapProofHash\":\"" + ProofHash + "\"}");
    }
}
