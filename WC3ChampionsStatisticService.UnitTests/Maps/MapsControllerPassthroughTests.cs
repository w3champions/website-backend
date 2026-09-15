using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using W3C.Contracts.Admin.Permission;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3C.Domain.MatchmakingService;
using W3C.Domain.UpdateService;
using W3C.Domain.UpdateService.Contracts;
using W3ChampionsStatisticService.Maps;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace WC3ChampionsStatisticService.Tests.Maps;

[TestFixture]
public class MapsControllerPassthroughTests
{
    /// <summary>ASP.NET Core's own response serializer defaults (camelCase).</summary>
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task GetMaps_ForwardsIncludeTemporaryToMatchmaking()
    {
        var handler = new ScriptedHttpHandler().On(HttpMethod.Get, "/maps", HttpStatusCode.OK, "{\"total\":0,\"items\":[]}");

        var result = await CreateController(handler).GetMaps(new GetMapsRequest { IncludeTemporary = true });

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var request = handler.LastRequest(HttpMethod.Get, "/maps");
        Assert.That(request.RequestUri!.Query, Does.Contain("includeTemporary=true"));
        Assert.That(request.Headers.Contains("x-admin-secret"), Is.True,
            "matchmaking ignores includeTemporary without the admin secret, so the admin checkbox would "
            + "silently show nothing (cross-plan review BLOCKER 3)");
    }

    [Test]
    public async Task CreateMap_StampsTheCallingAdminAsTheUploader()
    {
        var handler = new ScriptedHttpHandler().On(HttpMethod.Post, "/maps", HttpStatusCode.OK, "{\"id\":1}");

        await CreateController(handler).CreateMap(new MapContract { Name = "x", Uploader = "Spoofed#1" }, "Admin#1");

        Assert.That(JObject.Parse(handler.LastBody(HttpMethod.Post, "/maps"))["uploader"]!.Value<string>(),
            Is.EqualTo("Admin#1"));
    }

    [Test]
    public async Task UpdateMap_StampsTheCallingAdminAsTheUploader()
    {
        var handler = new ScriptedHttpHandler().On(HttpMethod.Put, "/maps/7", HttpStatusCode.OK, "{\"id\":7}");

        await CreateController(handler).UpdateMap(7, new MapContract { Name = "x", Uploader = "Spoofed#1" }, "Admin#1");

        Assert.That(JObject.Parse(handler.LastBody(HttpMethod.Put, "/maps/7"))["uploader"]!.Value<string>(),
            Is.EqualTo("Admin#1"));
    }

    [TestCase("CreateMap")]
    [TestCase("UpdateMap")]
    public async Task AdminMapWrites_ForwardOnlyTheAdminEditableFields(string action)
    {
        // Path, Temporary, FileState, LastHostedAt, SlotCount and OriginalFileName are matchmaking-owned: read for
        // the admin listing, never written back through the permanent-map routes.
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Post, "/maps", HttpStatusCode.OK, "{\"id\":7}")
            .On(HttpMethod.Put, "/maps/7", HttpStatusCode.OK, "{\"id\":7}");
        var controller = CreateController(handler);

        _ = action == "CreateMap"
            ? await controller.CreateMap(MapWithEveryField(), "Admin#1")
            : await controller.UpdateMap(7, MapWithEveryField(), "Admin#1");

        var body = JObject.Parse(handler.RequestBodies.Single());
        Assert.That(body.Properties().Select(p => p.Name), Is.EquivalentTo(new[]
        {
            "id", "name", "category", "maxTeams", "mappedForces", "gameMap", "teamSize", "disabled", "uploader",
        }));
        Assert.That(body["id"]!.Value<int>(), Is.EqualTo(7));
        Assert.That(body["name"]!.Value<string>(), Is.EqualTo("Echo Isles"));
        Assert.That(body["category"]!.Value<string>(), Is.EqualTo("1v1"));
        Assert.That(body["maxTeams"]!.Value<int>(), Is.EqualTo(2));
        Assert.That(body["teamSize"]!.Value<int>(), Is.EqualTo(1));
        Assert.That(body["disabled"]!.Value<bool>(), Is.True);
        Assert.That(body["mappedForces"]![0]!["slots"]![0]!["index"]!.Value<int>(), Is.EqualTo(1));
        Assert.That(body["gameMap"]!["path"]!.Value<string>(), Is.EqualTo(@"maps\W3Champions\EchoIsles.w3x"));
        Assert.That(body["uploader"]!.Value<string>(), Is.EqualTo("Admin#1"));
    }

    [Test]
    public async Task CreateMapFile_ForwardsTheCallingAdminAsUploadedBy_AndTheBodyVerbatim()
    {
        var handler = new ScriptedHttpHandler().On(HttpMethod.Post, "/api/content/maps", HttpStatusCode.OK,
            "{\"id\":\"f1\",\"mapId\":7,\"filePath\":\"W3Champions/v10/x.w3x\"}");
        var controller = CreateController(handler);
        controller.ControllerContext = new ControllerContext { HttpContext = MultipartRequest("opaque-form-body") };

        var result = await controller.CreateMapFile("Admin#1");

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var request = handler.LastRequest(HttpMethod.Post, "/api/content/maps");
        Assert.That(QueryHelpers.ParseQuery(request.RequestUri!.Query)["uploadedBy"].ToString(), Is.EqualTo("Admin#1"));
        Assert.That(handler.LastBody(HttpMethod.Post, "/api/content/maps"), Is.EqualTo("opaque-form-body"));
        Assert.That(request.Headers.Contains("x-admin-secret"), Is.True);
    }

    [Test]
    public void EveryMapsAdminActionStillRequiresTheMapsPermission()
    {
        foreach (var methodName in new[] { "GetMaps", "CreateMap", "UpdateMap", "GetMapFiles", "CreateMapFile", "GetMapFile", "DeleteMapFile" })
        {
            var method = typeof(MapsController).GetMethod(methodName)!;
            var attribute = method.GetCustomAttribute<BearerHasPermissionFilter>();

            Assert.That(attribute, Is.Not.Null, methodName + " must stay admin-gated");
            Assert.That(attribute!.Permission, Is.EqualTo(EPermission.Maps), methodName);
        }
    }

    [Test]
    public void TemporaryMapRoutesAreNotOnTheAdminController()
    {
        Assert.That(typeof(MapsController).GetMethods().Any(m => m.Name.Contains("Temporary")), Is.False,
            "player-facing temporary map routes live on TemporaryMapsController with player auth, never here");
    }

    [Test]
    public async Task GetTournamentMaps_NeverReServesUploaderOrTemporaryMapFields()
    {
        // The anonymous tournaments route relays matchmaking's map projection, which carries the uploader's
        // battleTag and (for any temporary row) its state once MapContract models them. Only the fields this
        // public route served before temporary maps existed may leave website-backend.
        var handler = new ScriptedHttpHandler().On(HttpMethod.Get, "/maps/tournaments", HttpStatusCode.OK,
            "{\"total\":1,\"items\":[{\"id\":7,\"name\":\"Echo Isles\",\"category\":\"1v1\",\"maxTeams\":2,\"teamSize\":1," +
            "\"disabled\":false,\"mappedForces\":[{\"team\":0,\"slots\":[{\"index\":0}]}]," +
            "\"gameMap\":{\"sha1\":\"abc\",\"path\":\"maps\\\\W3Champions\\\\EchoIsles.w3x\",\"name\":\"Echo Isles\"}," +
            "\"path\":\"W3Champions/EchoIsles.w3x\",\"uploader\":\"Admin#1\",\"temporary\":true,\"fileState\":\"present\"," +
            "\"lastHostedAt\":1757840000000,\"slotCount\":2,\"originalFileName\":\"EchoIsles.w3x\"," +
            "\"mapProof\":\"secret\",\"proofHash\":\"hash\"}]}");

        var result = await CreateController(handler).GetTournamentMaps();

        var json = JsonSerializer.Serialize(((OkObjectResult)result).Value, WebJson);
        using var document = JsonDocument.Parse(json);
        Assert.That(document.RootElement.GetProperty("total").GetInt32(), Is.EqualTo(1));
        var item = document.RootElement.GetProperty("items")[0];
        Assert.That(item.EnumerateObject().Select(p => p.Name),
            Is.EquivalentTo(new[] { "id", "name", "category", "maxTeams", "mappedForces", "gameMap", "teamSize", "disabled" }));
        Assert.That(item.GetProperty("id").GetInt32(), Is.EqualTo(7));
        Assert.That(item.GetProperty("name").GetString(), Is.EqualTo("Echo Isles"));
        Assert.That(item.GetProperty("teamSize").GetInt32(), Is.EqualTo(1));
        Assert.That(item.GetProperty("gameMap").GetProperty("sha1").GetString(), Is.EqualTo("abc"));
        Assert.That(item.GetProperty("mappedForces")[0].GetProperty("team").GetInt32(), Is.EqualTo(0));
        Assert.That(json, Does.Not.Contain("Admin#1").And.Not.Contain("secret").And.Not.Contain("hash").And.Not.Contain("present"));
    }

    [Test]
    public async Task GetTournamentMaps_KeepsAnEmptyUpstreamAnswerEmpty()
    {
        var handler = new ScriptedHttpHandler().On(HttpMethod.Get, "/maps/tournaments", HttpStatusCode.OK, "");

        var result = await CreateController(handler).GetTournamentMaps();

        Assert.That(((OkObjectResult)result).Value, Is.Null);
    }

    [Test]
    public void MapFileData_NeverWritesTheProofHashIntoWebsiteBackendResponses()
    {
        var json = JsonSerializer.Serialize(
            new MapFileData { Id = "f1", FilePath = "W3Champions/CustomGames/x-94ec3bda.w3x", MapProofHash = TemporaryMapClientTests.ProofHash },
            WebJson);

        Assert.That(json, Does.Not.Contain(TemporaryMapClientTests.ProofHash));
        Assert.That(json, Does.Contain("W3Champions/CustomGames/x-94ec3bda.w3x"));
    }

    private static MapContract MapWithEveryField() => new()
    {
        Id = 7,
        Name = "Echo Isles",
        Category = "1v1",
        MaxTeams = 2,
        MappedForces = [new MapForce { Team = 0, Slots = [new MapForceSlot { Index = 1, Color = 3 }] }],
        GameMap = new GameMap { Sha1 = "abc", Path = @"maps\W3Champions\EchoIsles.w3x" },
        teamSize = 1,
        Disabled = true,
        Uploader = "Spoofed#1",
        Path = "W3Champions/CustomGames/Echo Isles-94ec3bda.w3x",
        Temporary = true,
        FileState = "present",
        LastHostedAt = 1757840000000,
        SlotCount = 2,
        OriginalFileName = "Echo Isles.w3x",
    };

    private static DefaultHttpContext MultipartRequest(string body)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("website-backend.test");
        context.Request.Path = "/api/maps/7/files";
        context.Request.ContentType = "multipart/form-data; boundary=b";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return context;
    }

    private static MapsController CreateController(ScriptedHttpHandler handler)
    {
        var factory = new ScriptedHttpHandler.Factory(handler);
        return new MapsController(
            new MatchmakingServiceClient(factory), new UpdateServiceClient(factory), NullLogger<MapsController>.Instance);
    }
}
