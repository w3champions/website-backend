using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3C.Domain.MatchmakingService;
using W3C.Domain.MatchmakingService.Contracts;
using W3C.Domain.UpdateService;

namespace WC3ChampionsStatisticService.Tests.Maps;

[TestFixture]
public class TemporaryMapClientTests
{
    internal const string Sha1 = "94ec3bda2d9edd74bac37ec48edf4eb2b05138df";
    internal const string MapProof = "3b2724682f427727f8fa19236e04d3a5e3bdf218b7fe9c259c202e5264edbf4e";
    internal const string ProofHash = "6e60c9a01cfd09cec0b8493fe1ad9f2411ed21a1e1e58aab9eceeb3e9bac2bd6";
    internal const string FileKey = "W3Champions/CustomGames/Legion TD-94ec3bda.w3x";

    [Test]
    public async Task GetTemporaryMapBySha1_SendsAdminSecret_AndUnwrapsTheEnvelope()
    {
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Get, "/maps/temporary/by-sha1/", HttpStatusCode.OK,
                "{\"map\":{\"id\":5811,\"name\":\"Legion TD\",\"path\":\"" + FileKey + "\",\"temporary\":true," +
                "\"fileState\":\"present\",\"uploader\":\"peter#123\",\"lastHostedAt\":1757840000000,\"slotCount\":16," +
                "\"originalFileName\":\"Legion TD.w3x\",\"gameMap\":{\"sha1\":\"" + Sha1 + "\"}}}");

        var map = await Mm(handler).GetTemporaryMapBySha1(Sha1);

        Assert.That(map.Id, Is.EqualTo(5811));
        Assert.That(map.Path, Is.EqualTo(FileKey));
        Assert.That(map.Temporary, Is.True);
        Assert.That(map.FileState, Is.EqualTo(TemporaryMapFileStates.Present));
        Assert.That(map.Uploader, Is.EqualTo("peter#123"));
        Assert.That(map.LastHostedAt, Is.EqualTo(1757840000000));
        Assert.That(map.SlotCount, Is.EqualTo(16));
        Assert.That(map.OriginalFileName, Is.EqualTo("Legion TD.w3x"));
        Assert.That(map.GameMap.Sha1, Is.EqualTo(Sha1));
        Assert.That(handler.LastRequest(HttpMethod.Get, "/maps/temporary/by-sha1/").Headers.Contains("x-admin-secret"), Is.True);
        Assert.That(handler.LastRequest(HttpMethod.Get, "/maps/temporary/by-sha1/").RequestUri!.AbsolutePath,
            Does.EndWith("/maps/temporary/by-sha1/" + Sha1));
    }

    [Test]
    public async Task GetTemporaryMapBySha1_Returns404AsNull()
    {
        var handler = new ScriptedHttpHandler().On(HttpMethod.Get, "/maps/temporary/by-sha1/", HttpStatusCode.NotFound);

        Assert.That(await Mm(handler).GetTemporaryMapBySha1(Sha1), Is.Null);
    }

    [Test]
    public async Task GetTemporaryMapStateByProofHash_PostsTheProofHashInTheBody_AndReadsOnlyTheFileState()
    {
        // Appendix A.5 (revision 10): the proofHash travels in a JSON body, never in the URL, because the edge and
        // upstream proxies record request lines in their logs and do not record bodies.
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Post, "/maps/temporary/by-proof-hash", HttpStatusCode.OK, "{\"fileState\":\"deleted\"}");

        var state = await Mm(handler).GetTemporaryMapStateByProofHash(ProofHash);

        Assert.That(state.FileState, Is.EqualTo(TemporaryMapFileStates.Deleted));
        var request = handler.Requests.Single();
        Assert.That(request.Method, Is.EqualTo(HttpMethod.Post));
        Assert.That(request.RequestUri!.AbsolutePath, Does.EndWith("/maps/temporary/by-proof-hash"), "no trailing path segment");
        Assert.That(request.RequestUri.Query, Is.Empty, "no query");
        Assert.That(request.RequestUri.OriginalString, Does.Not.Contain(ProofHash));
        Assert.That(request.Headers.Contains("x-admin-secret"), Is.True);
        Assert.That(request.Content!.Headers.ContentType!.MediaType, Is.EqualTo("application/json"));
        var body = JObject.Parse(handler.RequestBodies.Single());
        Assert.That(body.Properties().Select(p => p.Name), Is.EqualTo(new[] { "proofHash" }), "the exact A.5 body key, and nothing else");
        Assert.That(body["proofHash"]!.Value<string>(), Is.EqualTo(ProofHash));
    }

    [Test]
    public void GetTemporaryMapStateByProofHash_A400_IsAContractViolation_WhoseMessageEchoesNothing()
    {
        // The key was validated before the request was built, so matchmaking refusing the body means the two services
        // disagree on the Appendix A.5 contract. Its 400 body can echo what it refused, so the message names the
        // status only, and the parser's own exception is never attached.
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Post, "/maps/temporary/by-proof-hash", HttpStatusCode.BadRequest,
                "{\"errors\":[{\"path\":\"proofHash\",\"msg\":\"Invalid value " + ProofHash + "\"}]}");

        var ex = Assert.ThrowsAsync<HttpRequestException>(() => Mm(handler).GetTemporaryMapStateByProofHash(ProofHash));

        Assert.That(handler.Requests, Has.Count.EqualTo(1));
        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(ex.Message, Does.Contain("400"));
        Assert.That(ex.Message, Does.Not.Contain(ProofHash).And.Not.Contain("Invalid value").And.Not.Contain("{"));
        Assert.That(ex.InnerException, Is.Null);
    }

    [Test]
    public async Task GetTemporaryMapByPath_UrlEncodesThePath()
    {
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Get, "/maps/temporary/by-path", HttpStatusCode.OK,
                "{\"map\":{\"id\":5811,\"path\":\"" + FileKey + "\"}}");

        await Mm(handler).GetTemporaryMapByPath(FileKey);

        var query = handler.LastRequest(HttpMethod.Get, "/maps/temporary/by-path").RequestUri!.Query;
        Assert.That(query, Does.Contain("W3Champions%2fCustomGames%2fLegion+TD-94ec3bda.w3x")
            .IgnoreCase.Or.Contain("W3Champions%2FCustomGames%2FLegion+TD-94ec3bda.w3x"));
    }

    [Test]
    public async Task CreateTemporaryMap_Posts201_AsCreated()
    {
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Post, "/maps/temporary", HttpStatusCode.Created,
                "{\"map\":{\"id\":5811,\"name\":\"Legion TD\",\"path\":\"" + FileKey + "\"}}");

        var result = await Mm(handler).CreateTemporaryMap(SampleCreateRequest());

        Assert.That(result.Created, Is.True);
        Assert.That(result.Map.Id, Is.EqualTo(5811));

        var body = JObject.Parse(handler.LastBody(HttpMethod.Post, "/maps/temporary"));
        Assert.That(body["sha1"]!.Value<string>(), Is.EqualTo(Sha1));
        Assert.That(body["uploader"]!.Value<string>(), Is.EqualTo("peter#123"));
        Assert.That(body["originalFileName"]!.Value<string>(), Is.EqualTo("Legion TD.w3x"));
        Assert.That(body["mapProof"]!.Value<string>(), Is.EqualTo(MapProof));
        Assert.That(body["maxTeams"]!.Value<int>(), Is.EqualTo(2));
        Assert.That(body["lobbyMode"]!.Value<string>(), Is.EqualTo("mapped-forces"));
        Assert.That(body["slotCount"]!.Value<int>(), Is.EqualTo(16));
        Assert.That(body["mappedForces"]![0]!["slots"]![0]!["index"]!.Value<int>(), Is.EqualTo(0));
        Assert.That(body["gameMap"]!["path"]!.Value<string>(),
            Is.EqualTo(@"maps\W3Champions\CustomGames\Legion TD-94ec3bda.w3x"));
        Assert.That(body["proofHash"], Is.Null, "mm derives proofHash itself; wb never sends it");
    }

    [Test]
    public async Task CreateTemporaryMap_Treats409AsAnExistingRecord()
    {
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Post, "/maps/temporary", HttpStatusCode.Conflict,
                "{\"map\":{\"id\":42,\"path\":\"" + FileKey + "\"}}");

        var result = await Mm(handler).CreateTemporaryMap(SampleCreateRequest());

        Assert.That(result.Created, Is.False);
        Assert.That(result.Map.Id, Is.EqualTo(42));
    }

    [Test]
    public void CreateTemporaryMap_ThrowsOnAnUpstreamError()
    {
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Post, "/maps/temporary", HttpStatusCode.InternalServerError, "{\"message\":\"boom\"}");

        var ex = Assert.ThrowsAsync<HttpRequestException>(() => Mm(handler).CreateTemporaryMap(SampleCreateRequest()));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
    }

    [Test]
    public async Task VerifyTemporaryMapProof_ReturnsIdentifiers_And404AsNull()
    {
        var found = new ScriptedHttpHandler()
            .On(HttpMethod.Post, "/maps/temporary/verify-proof", HttpStatusCode.OK,
                "{\"mapId\":5811,\"path\":\"" + FileKey + "\",\"fileState\":\"deleted\"}");

        var result = await Mm(found).VerifyTemporaryMapProof(ProofHash);

        Assert.That(result.MapId, Is.EqualTo(5811));
        Assert.That(result.Path, Is.EqualTo(FileKey));
        Assert.That(result.FileState, Is.EqualTo(TemporaryMapFileStates.Deleted));
        Assert.That(JObject.Parse(found.LastBody(HttpMethod.Post, "/maps/temporary/verify-proof"))["proofHash"]!.Value<string>(),
            Is.EqualTo(ProofHash));

        var missing = new ScriptedHttpHandler()
            .On(HttpMethod.Post, "/maps/temporary/verify-proof", HttpStatusCode.NotFound);
        Assert.That(await Mm(missing).VerifyTemporaryMapProof(ProofHash), Is.Null);
    }

    [Test]
    public async Task MarkTemporaryMapFileRestored_PostsTheProofAndSha1()
    {
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Post, "/maps/temporary/5811/file-restored", HttpStatusCode.OK,
                "{\"map\":{\"id\":5811,\"path\":\"" + FileKey + "\",\"fileState\":\"present\"}}");

        var map = await Mm(handler).MarkTemporaryMapFileRestored(5811,
            new TemporaryMapFileRestoredRequest { Sha1 = Sha1, Uploader = "peter#123", MapProof = MapProof });

        Assert.That(map.FileState, Is.EqualTo(TemporaryMapFileStates.Present));
        var body = JObject.Parse(handler.LastBody(HttpMethod.Post, "/maps/temporary/5811/file-restored"));
        Assert.That(body["mapProof"]!.Value<string>(), Is.EqualTo(MapProof));
        Assert.That(body["sha1"]!.Value<string>(), Is.EqualTo(Sha1));
        Assert.That(body["uploader"]!.Value<string>(), Is.EqualTo("peter#123"));
    }

    [Test]
    public void MarkTemporaryMapFileRestored_SurfacesA409AsAConflict()
    {
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Post, "/maps/temporary/5811/file-restored", HttpStatusCode.Conflict, "{}");

        var ex = Assert.ThrowsAsync<HttpRequestException>(() => Mm(handler).MarkTemporaryMapFileRestored(5811,
            new TemporaryMapFileRestoredRequest { Sha1 = Sha1, Uploader = "peter#123", MapProof = MapProof }));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task MarkTemporaryMapFileDeleted_PostsToTheIdRoute()
    {
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Post, "/maps/temporary/5811/file-deleted", HttpStatusCode.OK,
                "{\"map\":{\"id\":5811,\"fileState\":\"deleted\"}}");

        var map = await Mm(handler).MarkTemporaryMapFileDeleted(5811);

        Assert.That(map.FileState, Is.EqualTo(TemporaryMapFileStates.Deleted));
    }

    [Test]
    public async Task GetExpiredTemporaryMaps_SendsBeforeAndLimit()
    {
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Get, "/maps/temporary/expired", HttpStatusCode.OK,
                "{\"items\":[{\"id\":1,\"path\":\"" + FileKey + "\"},{\"id\":2,\"path\":\"W3Champions/CustomGames/b-00000000.w3x\"}]}");

        var expired = await Mm(handler).GetExpiredTemporaryMaps(1_700_000_000_000, 200);

        Assert.That(expired.Items, Has.Count.EqualTo(2));
        Assert.That(expired.Items[0].Id, Is.EqualTo(1));
        Assert.That(expired.Items[0].Path, Is.EqualTo(FileKey));
        var query = handler.LastRequest(HttpMethod.Get, "/maps/temporary/expired").RequestUri!.Query;
        Assert.That(query, Does.Contain("before=1700000000000"));
        Assert.That(query, Does.Contain("limit=200"));
    }

    [Test]
    public async Task GetMaps_ForwardsIncludeTemporary_OnlyWhenRequested()
    {
        var withFlag = new ScriptedHttpHandler().On(HttpMethod.Get, "/maps", HttpStatusCode.OK, "{\"total\":0,\"items\":[]}");
        await Mm(withFlag).GetMaps(new GetMapsRequest { Filter = "legion td", IncludeTemporary = true });
        var query = withFlag.LastRequest(HttpMethod.Get, "/maps").RequestUri!.Query;
        Assert.That(query, Does.Contain("includeTemporary=true"));
        Assert.That(query, Does.Contain("filter=legion+td").Or.Contain("filter=legion%20td"),
            "the filter must be URL-encoded like every other query parameter on this client");

        var withoutFlag = new ScriptedHttpHandler().On(HttpMethod.Get, "/maps", HttpStatusCode.OK, "{\"total\":0,\"items\":[]}");
        await Mm(withoutFlag).GetMaps(new GetMapsRequest { Filter = "x" });
        Assert.That(withoutFlag.LastRequest(HttpMethod.Get, "/maps").RequestUri!.Query, Does.Not.Contain("includeTemporary"));
    }

    [Test]
    public async Task GetMaps_SendsTheAdminSecretAndIncludeTemporary()
    {
        // matchmaking gates includeTemporary on isAdminRequest (presentedSecrets + matchesSecret). Drop
        // the header and mm silently answers with the permanent-only list, making the website's
        // "Show temporary maps" checkbox a permanent no-op with nothing logged anywhere.
        var handler = new ScriptedHttpHandler().On(HttpMethod.Get, "/maps", HttpStatusCode.OK, "{\"total\":0,\"items\":[]}");

        await Mm(handler).GetMaps(new GetMapsRequest { IncludeTemporary = true });

        var request = handler.LastRequest(HttpMethod.Get, "/maps");
        Assert.That(request.Headers.Contains("x-admin-secret"), Is.True,
            "includeTemporary is admin-only: without x-admin-secret matchmaking ignores it");
        Assert.That(request.RequestUri!.Query, Does.Contain("includeTemporary=true"));
    }

    [Test]
    public async Task GetMaps_SendsTheAdminSecretEvenForAPermanentOnlyListing()
    {
        var handler = new ScriptedHttpHandler().On(HttpMethod.Get, "/maps", HttpStatusCode.OK, "{\"total\":0,\"items\":[]}");

        await Mm(handler).GetMaps(new GetMapsRequest { Filter = "x" });

        Assert.That(handler.LastRequest(HttpMethod.Get, "/maps").Headers.Contains("x-admin-secret"), Is.True,
            "the header is behaviour-neutral without includeTemporary; keeping it unconditional avoids a "
            + "second code path that could drift");
    }

    [Test]
    public async Task UploadTemporaryMapAsync_PostsTheFormFieldsAndAdminSecret()
    {
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Post, "/api/content/maps", HttpStatusCode.OK,
                "{\"id\":\"66f0\",\"mapId\":0,\"filePath\":\"" + FileKey + "\"," +
                "\"mapProofHash\":\"" + ProofHash + "\",\"metaData\":{\"sha1\":\"" + Sha1 + "\",\"twelve_p\":false}}");

        await using var bytes = new MemoryStream(Encoding.UTF8.GetBytes("abc"));
        var result = await Us(handler).UploadTemporaryMapAsync(
            bytes, "CustomGames/Legion TD-94ec3bda.w3x", 0, "peter#123", CancellationToken.None);

        Assert.That(result.FilePath, Is.EqualTo(FileKey));
        Assert.That(result.MapProofHash, Is.EqualTo(ProofHash));
        Assert.That(result.MetaData.Sha1, Is.EqualTo(Sha1));

        var request = handler.LastRequest(HttpMethod.Post, "/api/content/maps");
        Assert.That(request.Headers.Contains("x-admin-secret"), Is.True);
        Assert.That(request.Content!.Headers.ContentType!.MediaType, Is.EqualTo("multipart/form-data"));

        // .NET 8 writes unquoted `name=` tokens, so the sections are parsed rather than string-matched.
        var sections = await ReadFormSections(request, handler.LastBody(HttpMethod.Post, "/api/content/maps"));
        Assert.That(sections.Keys, Is.EquivalentTo(new[] { "mapId", "fileName", "uploadedBy", "mapFile" }));
        Assert.That(sections["mapId"].Content, Is.EqualTo("0"));
        Assert.That(sections["fileName"].Content, Is.EqualTo("CustomGames/Legion TD-94ec3bda.w3x"));
        Assert.That(sections["uploadedBy"].Content, Is.EqualTo("peter#123"));
        Assert.That(sections["mapFile"].Content, Is.EqualTo("abc"));
        Assert.That(sections["mapFile"].FileName, Is.EqualTo("Legion TD-94ec3bda.w3x"));
    }

    [Test]
    public void UploadTemporaryMapAsync_SurfacesA409AsAConflictHttpRequestException()
    {
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Post, "/api/content/maps", HttpStatusCode.Conflict, "{\"message\":\"File already exists\"}");

        using var bytes = new MemoryStream(Encoding.UTF8.GetBytes("abc"));
        var client = Us(handler);

        var ex = Assert.ThrowsAsync<HttpRequestException>(
            () => client.UploadTemporaryMapAsync(bytes, "CustomGames/x.w3x", 0, "peter#123", CancellationToken.None));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(ex.Message, Is.EqualTo("File already exists"));
    }

    [Test]
    public async Task DeleteMapFileByPathAsync_UsesTheFilePathRoute()
    {
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Delete, "/api/content/maps/file", HttpStatusCode.NoContent, "");

        await Us(handler).DeleteMapFileByPathAsync(FileKey, CancellationToken.None);

        var request = handler.LastRequest(HttpMethod.Delete, "/api/content/maps/file");
        Assert.That(request.Headers.Contains("x-admin-secret"), Is.True);
        Assert.That(request.RequestUri!.AbsolutePath, Does.EndWith("/api/content/maps/file"));
        Assert.That(QueryHelpers.ParseQuery(request.RequestUri.Query)["filePath"].ToString(), Is.EqualTo(FileKey));
    }

    [Test]
    public void DeleteMapFileByPathAsync_ThrowsWithTheUpstreamStatus()
    {
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Delete, "/api/content/maps/file", HttpStatusCode.BadRequest, "{\"message\":\"bad path\"}");

        var ex = Assert.ThrowsAsync<HttpRequestException>(
            () => Us(handler).DeleteMapFileByPathAsync(FileKey, CancellationToken.None));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public void DeleteMapFileByPathAsync_NamesTheOperationAndStatus_NeverThePath()
    {
        // The path passes the shape check with a control character in it (a log line admits none), and callers log
        // the exception: its message carries the operation and the status, and the caller renders the gated path itself.
        const string pathWithLineBreak = "W3Champions/CustomGames/x-94ec3bda\r\nforged.w3x";
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Delete, "/api/content/maps/file", HttpStatusCode.InternalServerError, "");

        var ex = Assert.ThrowsAsync<HttpRequestException>(
            () => Us(handler).DeleteMapFileByPathAsync(pathWithLineBreak, CancellationToken.None));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.That(ex.Message, Does.Contain("500").And.Contain("delet"));
        Assert.That(ex.Message, Does.Not.Contain("forged").And.Not.Contain("\r").And.Not.Contain("\n").And.Not.Contain("CustomGames"));
    }

    [Test]
    public async Task ListMapFilesAsync_SendsEveryPagingParameter()
    {
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Get, "/api/content/maps/files", HttpStatusCode.OK,
                "{\"files\":[{\"filePath\":\"" + FileKey + "\",\"sizeBytes\":31301181,\"modifiedAt\":\"2026-09-01T10:00:00Z\"}]," +
                "\"next\":\"W3Champions/CustomGames/z.w3x\"}");

        var listing = await Us(handler).ListMapFilesAsync(
            "W3Champions/CustomGames/", 24, "W3Champions/CustomGames/a.w3x", 500, CancellationToken.None);

        Assert.That(listing.Files, Has.Count.EqualTo(1));
        Assert.That(listing.Files[0].FilePath, Is.EqualTo(FileKey));
        Assert.That(listing.Files[0].SizeBytes, Is.EqualTo(31301181));
        Assert.That(listing.Files[0].ModifiedAt, Is.EqualTo(new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc)));
        Assert.That(listing.Next, Is.EqualTo("W3Champions/CustomGames/z.w3x"));

        var query = QueryHelpers.ParseQuery(handler.LastRequest(HttpMethod.Get, "/api/content/maps/files").RequestUri!.Query);
        Assert.That(query["prefix"].ToString(), Is.EqualTo("W3Champions/CustomGames/"));
        Assert.That(query["olderThanHours"].ToString(), Is.EqualTo("24"));
        Assert.That(query["after"].ToString(), Is.EqualTo("W3Champions/CustomGames/a.w3x"));
        Assert.That(query["limit"].ToString(), Is.EqualTo("500"));
    }

    [Test]
    public async Task ListMapFilesAsync_OmitsAfterOnTheFirstPage()
    {
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Get, "/api/content/maps/files", HttpStatusCode.OK, "{\"files\":[],\"next\":null}");

        await Us(handler).ListMapFilesAsync("W3Champions/CustomGames/", 24, null, 500, CancellationToken.None);

        Assert.That(handler.LastRequest(HttpMethod.Get, "/api/content/maps/files").RequestUri!.Query,
            Does.Not.Contain("after="));
    }

    internal static CreateTemporaryMapRequest SampleCreateRequest() => new()
    {
        Sha1 = Sha1,
        Uploader = "peter#123",
        OriginalFileName = "Legion TD.w3x",
        MapProof = MapProof,
        MaxTeams = 2,
        SlotCount = 16,
        LobbyMode = "mapped-forces",
        MappedForces = [new MapForce { Team = 0, Slots = [new MapForceSlot { Index = 0, Color = 0 }] }],
        GameMap = new GameMap { Sha1 = Sha1, Name = "Legion TD", Path = @"maps\W3Champions\CustomGames\Legion TD-94ec3bda.w3x" },
    };

    internal static MatchmakingServiceClient Mm(ScriptedHttpHandler handler)
        => new(new ScriptedHttpHandler.Factory(handler));

    internal static UpdateServiceClient Us(ScriptedHttpHandler handler)
        => new(new ScriptedHttpHandler.Factory(handler));

    /// <summary>Parses a captured multipart body into name → (content, file name), quote-agnostic.</summary>
    internal static async Task<Dictionary<string, (string Content, string FileName)>> ReadFormSections(
        HttpRequestMessage request, string body)
    {
        var boundary = HeaderUtilities.RemoveQuotes(
            request.Content!.Headers.ContentType!.Parameters.Single(p => p.Name == "boundary").Value).Value;
        var reader = new MultipartReader(boundary, new MemoryStream(Encoding.UTF8.GetBytes(body)));
        var sections = new Dictionary<string, (string Content, string FileName)>();

        for (var section = await reader.ReadNextSectionAsync(); section != null; section = await reader.ReadNextSectionAsync())
        {
            var disposition = section.GetContentDispositionHeader()!;
            using var content = new StreamReader(section.Body);
            sections[HeaderUtilities.RemoveQuotes(disposition.Name).Value!] = (
                await content.ReadToEndAsync(),
                HeaderUtilities.RemoveQuotes(disposition.FileName).Value);
        }

        return sections;
    }
}
