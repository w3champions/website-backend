using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
using W3C.Domain.MatchmakingService.Contracts;
using W3C.Domain.UpdateService;
using W3C.Domain.UpdateService.Contracts;
using W3ChampionsStatisticService.Maps;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using W3ChampionsStatisticService.WebApi.ExceptionFilters;

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
            + "silently show nothing");
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
    public void CreateMapFile_StreamsItsBodyLikeTheTemporaryUpload()
    {
        // The passthrough forwards the multipart body as a stream; binding its battleTag parameter must not let the
        // form value provider read that body first (the whole body, buffered, before the permission filter runs).
        var passthrough = typeof(MapsController).GetMethod(nameof(MapsController.CreateMapFile))!;

        Assert.That(passthrough.GetCustomAttribute<DisableFormValueModelBindingAttribute>(), Is.Not.Null, "model binding must stay off the body");
    }

    [Test]
    public void CreateMapFile_CarriesTheSharedTransportBodyLimit_AndNoOtherActionDoes()
    {
        // update-service accepts a map file up to TransportBodyBytes; under the global Kestrel ceiling alone this
        // passthrough would refuse an admin's file between 128 MiB and 256 MiB with a 413 although update-service takes
        // it. The same resource filter as on the temporary upload raises the ceiling (and sets the data-rate floor) for
        // this action only: nothing else on the controller streams a body.
        var actions = typeof(MapsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>() == null)
            .ToArray();

        Assert.That(actions, Has.Length.GreaterThan(1));
        Assert.That(actions.Where(m => m.GetCustomAttribute<TemporaryMapUploadBodyLimitAttribute>() != null).Select(m => m.Name),
            Is.EqualTo(new[] { nameof(MapsController.CreateMapFile) }));
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

    [TestCase("GetMaps", HttpStatusCode.InternalServerError, "{\"total\":0}", StatusCodes.Status500InternalServerError)]
    [TestCase("GetTournamentMaps", HttpStatusCode.InternalServerError, "{\"total\":0}", StatusCodes.Status500InternalServerError)]
    [TestCase("GetMaps", HttpStatusCode.OK, "<html>maintenance</html>", StatusCodes.Status502BadGateway)]
    [TestCase("GetTournamentMaps", HttpStatusCode.OK, "", StatusCodes.Status502BadGateway)]
    [TestCase("GetMaps", HttpStatusCode.Unauthorized, "{\"message\":\"refused\"}", StatusCodes.Status502BadGateway)]
    [TestCase("GetMaps", HttpStatusCode.Forbidden, "{\"message\":\"refused\"}", StatusCodes.Status502BadGateway)]
    [TestCase("GetTournamentMaps", HttpStatusCode.Unauthorized, "{\"message\":\"refused\"}", StatusCodes.Status502BadGateway)]
    [TestCase("GetTournamentMaps", HttpStatusCode.Forbidden, "{\"message\":\"refused\"}", StatusCodes.Status502BadGateway)]
    [TestCase("GetMaps", HttpStatusCode.ProxyAuthenticationRequired, "{\"message\":\"refused\"}", StatusCodes.Status502BadGateway)]
    [TestCase("GetTournamentMaps", HttpStatusCode.ProxyAuthenticationRequired, "{\"message\":\"refused\"}", StatusCodes.Status502BadGateway)]
    [TestCase("GetMaps", HttpStatusCode.OK, "{\"total\":0}", StatusCodes.Status502BadGateway)]
    [TestCase("GetTournamentMaps", HttpStatusCode.OK, "{\"total\":0}", StatusCodes.Status502BadGateway)]
    [TestCase("GetMaps", HttpStatusCode.OK, "{\"total\":1,\"items\":[null]}", StatusCodes.Status502BadGateway)]
    [TestCase("GetTournamentMaps", HttpStatusCode.OK, "{\"total\":1,\"items\":[null]}", StatusCodes.Status502BadGateway)]
    public void MapListingActions_WhenMatchmakingFails_AnswerAnUpstreamFailure_NeverAnEmptyList(
        string action, HttpStatusCode upstreamStatus, string body, int expectedStatus)
    {
        // Neither action catches: the global HttpRequestExceptionFilter answers the failure. This includes the
        // anonymous tournaments route, which used to answer 200 with an empty listing. A matchmaking 401/403/407 is
        // website-backend's own admin-secret or proxy configuration failing, so neither caller may read it as their
        // own. A 200 whose listing has no items array, or holds a null row, is a contract violation, never a listing.
        var handler = new ScriptedHttpHandler().On(HttpMethod.Get, "/maps", upstreamStatus, body);
        var controller = CreateController(handler);

        var ex = Assert.ThrowsAsync<HttpRequestException>(() => action == "GetMaps"
            ? controller.GetMaps(new GetMapsRequest())
            : controller.GetTournamentMaps());

        Assert.That(HttpRequestExceptionFilter.StatusCodeOf(ex!), Is.EqualTo(expectedStatus));
    }

    [TestCase("GetMaps")]
    [TestCase("GetTournamentMaps")]
    public async Task MapListingActions_WithAWellFormedEmptyListing_Answer200(string action)
    {
        var handler = new ScriptedHttpHandler().On(HttpMethod.Get, "/maps", HttpStatusCode.OK, "{\"total\":0,\"items\":[]}");
        var controller = CreateController(handler);

        var result = action == "GetMaps"
            ? await controller.GetMaps(new GetMapsRequest())
            : await controller.GetTournamentMaps();

        var json = JsonSerializer.Serialize(((OkObjectResult)result).Value, WebJson);
        using var document = JsonDocument.Parse(json);
        Assert.That(document.RootElement.GetProperty("total").GetInt32(), Is.EqualTo(0));
        Assert.That(document.RootElement.GetProperty("items").GetArrayLength(), Is.EqualTo(0));
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

    [Test]
    public async Task GetMaps_ReServesTheTemporaryMapFields_ButNoProofMember()
    {
        // The admin listing must keep reading every temporary-map field matchmaking returns, and must never
        // re-serve a credential-bearing member, even if matchmaking's row carries one.
        var handler = new ScriptedHttpHandler().On(HttpMethod.Get, "/maps", HttpStatusCode.OK,
            "{\"total\":1,\"items\":[{\"id\":5811,\"name\":\"Legion TD\",\"path\":\"" + TemporaryMapClientTests.FileKey + "\"," +
            "\"temporary\":true,\"fileState\":\"present\",\"uploader\":\"peter#123\",\"lastHostedAt\":1757840000000," +
            "\"slotCount\":16,\"originalFileName\":\"Legion TD.w3x\"," +
            "\"mapProof\":\"" + TemporaryMapClientTests.MapProof + "\",\"proofHash\":\"" + TemporaryMapClientTests.ProofHash + "\"," +
            "\"gameMap\":{\"sha1\":\"" + TemporaryMapClientTests.Sha1 + "\",\"mapProof\":\"" + TemporaryMapClientTests.MapProof + "\"," +
            "\"proofHash\":\"" + TemporaryMapClientTests.ProofHash + "\"}}]}");

        var result = await CreateController(handler).GetMaps(new GetMapsRequest { IncludeTemporary = true });

        var json = JsonSerializer.Serialize(((OkObjectResult)result).Value, WebJson);
        Assert.That(json, Does.Not.Contain(TemporaryMapClientTests.MapProof).And.Not.Contain(TemporaryMapClientTests.ProofHash));
        Assert.That(json, Does.Not.Contain("proof").IgnoreCase);
        using var document = JsonDocument.Parse(json);
        var item = document.RootElement.GetProperty("items")[0];
        Assert.That(item.GetProperty("path").GetString(), Is.EqualTo(TemporaryMapClientTests.FileKey));
        Assert.That(item.GetProperty("temporary").GetBoolean(), Is.True);
        Assert.That(item.GetProperty("fileState").GetString(), Is.EqualTo("present"));
        Assert.That(item.GetProperty("uploader").GetString(), Is.EqualTo("peter#123"));
        Assert.That(item.GetProperty("lastHostedAt").GetInt64(), Is.EqualTo(1757840000000));
        Assert.That(item.GetProperty("slotCount").GetInt32(), Is.EqualTo(16));
        Assert.That(item.GetProperty("originalFileName").GetString(), Is.EqualTo("Legion TD.w3x"));
        Assert.That(item.GetProperty("gameMap").GetProperty("sha1").GetString(), Is.EqualTo(TemporaryMapClientTests.Sha1));
    }

    private static IEnumerable<TestCaseData> ReServedTypes()
    {
        // Every type website-backend writes into a response on the map routes, including the pre-check state
        // that is relayed to players verbatim.
        yield return new TestCaseData(typeof(MapContract));
        yield return new TestCaseData(typeof(GetMapsResponse));
        yield return new TestCaseData(typeof(PublicMapsResponse));
        yield return new TestCaseData(typeof(MapFileData));
        yield return new TestCaseData(typeof(TemporaryMapStateResponse));
    }

    [TestCaseSource(nameof(ReServedTypes))]
    public void ReServedType_SerialisesNoProofMember(Type root)
    {
        var members = SerialisedMembers(root).ToList();

        Assert.That(members, Is.Not.Empty);
        Assert.That(members.Where(m => m.JsonName.Contains("proof", StringComparison.OrdinalIgnoreCase)).Select(m => m.Path), Is.Empty,
            "a credential-bearing member must never be re-served; model it as [JsonIgnore] if it must be read");
    }

    [Test]
    public void SerialisedMemberWalk_ReachesNestedTypesAndSkipsIgnoredMembers()
    {
        var paths = SerialisedMembers(typeof(GetMapsResponse)).Select(m => m.Path).ToList();

        Assert.That(paths, Does.Contain("GetMapsResponse.Items.GameMap.Sha1"));
        Assert.That(paths, Does.Contain("GetMapsResponse.Items.MappedForces.Slots.Index"));
        Assert.That(SerialisedMembers(typeof(MapFileData)).Select(m => m.Path), Does.Not.Contain("MapFileData.MapProofHash"),
            "MapFileData.MapProofHash is read from update-service and must stay [JsonIgnore] for website-backend's own responses");
    }

    /// <summary>Every property System.Text.Json writes for <paramref name="root"/>, walking W3C types, arrays and lists.</summary>
    private static IEnumerable<(string Path, string JsonName, PropertyInfo Property)> SerialisedMembers(Type root)
    {
        var visited = new HashSet<Type>();
        var pending = new Stack<(Type Type, string Path)>();
        pending.Push((root, root.Name));
        while (pending.TryPop(out var current))
        {
            if (!visited.Add(current.Type))
            {
                continue;
            }

            foreach (var property in current.Type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: JsonIgnoreCondition.Always })
                {
                    continue;
                }

                var path = current.Path + "." + property.Name;
                yield return (path, property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name, property);

                var memberType = ElementTypeOf(property.PropertyType);
                if (memberType.Namespace?.StartsWith("W3C", StringComparison.Ordinal) == true
                    || memberType.Namespace?.StartsWith("W3ChampionsStatisticService", StringComparison.Ordinal) == true)
                {
                    pending.Push((memberType, path));
                }
            }
        }
    }

    private static Type ElementTypeOf(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType()!;
        }

        return type.IsGenericType ? type.GetGenericArguments()[0] : type;
    }

    // ---- Members renamed on the wire: read from matchmaking with Newtonsoft, written to the website with STJ --------

    [Test]
    public void GameMapForce_WritesPlayerSetUnderItsSnakeCaseName_AsTheWebsiteReadsIt()
    {
        // MVC writes responses with System.Text.Json, which honours only [JsonPropertyName]; the matchmaking listing
        // is read with Newtonsoft, which honours [JsonProperty]. A member renamed for one serialiser only leaves this
        // service camel-casing what the admin map file details read as force.player_set.
        var map = new MapContract
        {
            Id = 7,
            GameMap = new GameMap { Forces = [new GameMapForce { Name = "Force 1", Flags = 0, PlayerSet = 3 }] },
        };

        var json = JsonSerializer.Serialize(new GetMapsResponse { Total = 1, Items = [map] }, WebJson);

        using var document = JsonDocument.Parse(json);
        var force = document.RootElement.GetProperty("items")[0].GetProperty("gameMap").GetProperty("forces")[0];
        Assert.That(force.EnumerateObject().Select(p => p.Name), Is.EquivalentTo(new[] { "name", "flags", "player_set" }));
        Assert.That(force.GetProperty("player_set").GetInt64(), Is.EqualTo(3));
        Assert.That(json, Does.Not.Contain("playerSet"));
    }

    [Test]
    public void GameMapForce_StillReadsPlayerSetFromMatchmaking()
    {
        var map = Newtonsoft.Json.JsonConvert.DeserializeObject<GameMap>(
            "{\"sha1\":\"abc\",\"forces\":[{\"name\":\"Force 1\",\"flags\":0,\"player_set\":3}]}");

        Assert.That(map!.Forces, Has.Length.EqualTo(1));
        Assert.That(map.Forces[0].PlayerSet, Is.EqualTo(3));
    }

    [Test]
    public void EveryRenamedMemberOfTheMapListing_IsWrittenUnderTheNameItIsReadWith()
    {
        // The [JsonProperty] rename is what matchmaking sends; the [JsonPropertyName] twin is what this service writes.
        // Walking the listing's whole graph makes a renamed member added without its twin a failing test.
        var renamed = SerialisedMembers(typeof(GetMapsResponse))
            .Select(m => (
                m.Path,
                Read: m.Property.GetCustomAttribute<Newtonsoft.Json.JsonPropertyAttribute>()?.PropertyName,
                Written: m.Property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name))
            .Where(m => m.Read != null)
            .ToList();

        Assert.That(renamed.Select(m => m.Path), Is.EquivalentTo(new[]
        {
            "GetMapsResponse.Items.GameMap.SuggestedPlayers",
            "GetMapsResponse.Items.GameMap.NumPlayers",
            "GetMapsResponse.Items.GameMap.TwelveP",
            "GetMapsResponse.Items.GameMap.Forces.PlayerSet",
        }));
        Assert.That(renamed.Where(m => m.Written != m.Read).Select(m => $"{m.Path}: read as {m.Read}, written as {m.Written ?? "(camelCase)"}"),
            Is.Empty, "a member renamed for Newtonsoft needs the same [JsonPropertyName] for System.Text.Json");
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
