using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using W3C.Domain.MatchmakingService.Contracts;
using static WC3ChampionsStatisticService.Tests.Maps.TemporaryMapClientTests;

namespace WC3ChampionsStatisticService.Tests.Maps;

/// <summary>
/// A body that breaks the Appendix A.5/A.7 contract on an otherwise expected status (2xx, or 409 on create) is an
/// upstream fault. It must surface as an HttpRequestException carrying that status, never as a JsonException, a
/// null record or an empty page, and its message must quote neither the body nor a URI.
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
        string[] BodiesWithoutTheRecord);

    private static readonly Route[] Routes =
    [
        new("GetTemporaryMapBySha1", HttpMethod.Get, "/maps/temporary/by-sha1/", HttpStatusCode.OK,
            h => Mm(h).GetTemporaryMapBySha1(Sha1), ["{}", "{\"map\":null}", "{\"map\":\"" + BodyMarker + "\"}"]),
        new("GetTemporaryMapStateByProofHash", HttpMethod.Get, "/maps/temporary/by-proof-hash/", HttpStatusCode.OK,
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
        new("GetExpiredTemporaryMaps", HttpMethod.Get, "/maps/temporary/expired", HttpStatusCode.OK,
            h => Mm(h).GetExpiredTemporaryMaps(1, 200), ["{}", "{\"items\":null}", "{\"items\":{}}"]),
        new("UploadTemporaryMapAsync", HttpMethod.Post, "/api/content/maps", HttpStatusCode.OK,
            UploadAbc, []),
        new("ListMapFilesAsync", HttpMethod.Get, "/api/content/maps/files", HttpStatusCode.OK,
            h => Us(h).ListMapFilesAsync("W3Champions/CustomGames/", 24, null, 500, CancellationToken.None),
            ["{}", "{\"files\":null}", "{\"next\":\"W3Champions/CustomGames/z.w3x\"}"]),
    ];

    private static IEnumerable<TestCaseData> ContractViolations()
    {
        foreach (var route in Routes)
        {
            foreach (var (label, body) in UnreadableBodies)
            {
                yield return new TestCaseData(route.Name, body).SetName($"{route.Name}_{(int)route.Status}Body{label}");
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
}
