using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using W3C.Contracts.Admin.Permission;
using W3C.Domain.MatchmakingService;
using W3C.Domain.UpdateService;
using W3ChampionsStatisticService.Maps;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using WC3ChampionsStatisticService.Tests.Admin;
using static WC3ChampionsStatisticService.Tests.Maps.RecomputeJson;

namespace WC3ChampionsStatisticService.Tests.Maps;

[TestFixture]
public class MapsControllerRecomputeTests
{
    private const string GamePath = @"maps\W3Champions\(3)NomadIsles_fix.w3x";
    private const string FilePath = "W3Champions/(3)NomadIsles_fix.w3x";
    private static readonly string Sha1 = new('a', 40);

    private static MapsController CreateController(RoutingHandler handler)
    {
        var factory = new RecordingHttpClientFactory(handler);
        var matchmaking = new MatchmakingServiceClient(factory);
        var update = new UpdateServiceClient(factory);
        return new MapsController(matchmaking, update, new MapMetadataRecomputeService(matchmaking, update));
    }

    private static RoutingHandler Healthy() => new RoutingHandler()
        .On(HttpMethod.Get, RoutingHandler.MmMapsPath, HttpStatusCode.OK, MmMaps(MmMap(7, "Nomad Isles", GamePath, Sha1, 1, 2)))
        .On(HttpMethod.Post, RoutingHandler.UpdateRecomputePath, body =>
            RoutingHandler.Json(HttpStatusCode.OK, Recompute((bool)JObject.Parse(body)["dryRun"]!, Result(FilePath, Sha1, "changed", 1, 3, 2, 4))))
        .On(HttpMethod.Post, RoutingHandler.MmGameMapMetadataPath, MmPatchEcho());

    [Test]
    public void RecomputeRequiresTheMapsPermissionAndIsAPostUnderApiMaps()
    {
        var method = typeof(MapsController).GetMethod(nameof(MapsController.RecomputeMapMetadata))!;

        var permission = method.GetCustomAttribute<BearerHasPermissionFilter>();
        Assert.That(permission, Is.Not.Null);
        Assert.That(permission!.Permission, Is.EqualTo(EPermission.Maps));

        var post = method.GetCustomAttribute<HttpPostAttribute>();
        Assert.That(post!.Template, Is.EqualTo("metadata/recompute"));
        Assert.That(typeof(MapsController).GetCustomAttribute<RouteAttribute>()!.Template, Is.EqualTo("api/maps"));

        var fromBody = method.GetParameters().Single().GetCustomAttribute<FromBodyAttribute>();
        Assert.That(fromBody!.EmptyBodyBehavior, Is.EqualTo(EmptyBodyBehavior.Allow), "an empty body is a dry run");
    }

    [Test]
    public void BodyDefaultsToDryRun()
    {
        Assert.That(new MapMetadataRecomputeBody().DryRun, Is.True);
    }

    [Test]
    public async Task MissingBody_IsADryRun()
    {
        var handler = Healthy();

        var result = await CreateController(handler).RecomputeMapMetadata(null);

        var report = (MapMetadataRecomputeReport)((OkObjectResult)result).Value!;
        Assert.That(report.DryRun, Is.True);
        Assert.That((bool)JObject.Parse(handler.BodiesFor(HttpMethod.Post, RoutingHandler.UpdateRecomputePath)[0])["dryRun"]!, Is.True);
        Assert.That(handler.RequestsFor(HttpMethod.Post, RoutingHandler.MmGameMapMetadataPath), Is.Empty);
    }

    [Test]
    public async Task NonDryRun_ReturnsTheCombinedReport()
    {
        var handler = Healthy();

        var result = await CreateController(handler).RecomputeMapMetadata(new MapMetadataRecomputeBody { DryRun = false });

        var report = (MapMetadataRecomputeReport)((OkObjectResult)result).Value!;
        Assert.That(report.DryRun, Is.False);
        Assert.That(report.Maps.Single().MmPatched, Is.True);
        Assert.That(handler.RequestsFor(HttpMethod.Post, RoutingHandler.MmGameMapMetadataPath), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task MapIds_AreForwarded()
    {
        var handler = Healthy();

        var result = await CreateController(handler).RecomputeMapMetadata(new MapMetadataRecomputeBody { DryRun = true, MapIds = [7, 999] });

        var report = (MapMetadataRecomputeReport)((OkObjectResult)result).Value!;
        Assert.That(report.Maps.Select(m => m.MmMapId), Is.EqualTo(new[] { 7 }));
        Assert.That(report.UnknownMapIds, Is.EqualTo(new[] { 999 }));
    }

    [Test]
    public async Task EmptyMapIds_IsABadRequestInsteadOfAFullRun()
    {
        var handler = Healthy();

        var result = await CreateController(handler).RecomputeMapMetadata(new MapMetadataRecomputeBody { DryRun = true, MapIds = [] });

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        Assert.That(handler.Calls, Is.Empty);
    }

    [Test]
    public async Task UpdateServiceFailure_Is502WithoutPatchingMatchmaking()
    {
        var handler = Healthy().On(HttpMethod.Post, RoutingHandler.UpdateRecomputePath, HttpStatusCode.Unauthorized, "{\"message\":\"no\"}");

        var result = await CreateController(handler).RecomputeMapMetadata(new MapMetadataRecomputeBody { DryRun = false });

        var objectResult = (ObjectResult)result;
        Assert.That(objectResult.StatusCode, Is.EqualTo(StatusCodes502));
        var body = JObject.FromObject(objectResult.Value!);
        Assert.That((string)body["error"], Is.EqualTo(MapsController.RecomputeUpstreamError), "no upstream detail leaks into the answer");
        Assert.That((bool)body["matchmakingPatched"]!, Is.False);
        Assert.That((string)body["service"], Is.EqualTo(MapMetadataRecomputeUpstreamException.UpdateService));
        Assert.That((int?)body["upstreamStatus"], Is.EqualTo(401));
        Assert.That(handler.RequestsFor(HttpMethod.Post, RoutingHandler.MmGameMapMetadataPath), Is.Empty);
    }

    [Test]
    public async Task MatchmakingPatchFailure_Is502CarryingTheFullReport()
    {
        var handler = Healthy().On(HttpMethod.Post, RoutingHandler.MmGameMapMetadataPath, HttpStatusCode.InternalServerError, "{}");

        var result = await CreateController(handler).RecomputeMapMetadata(new MapMetadataRecomputeBody { DryRun = false });

        var objectResult = (ObjectResult)result;
        Assert.That(objectResult.StatusCode, Is.EqualTo(StatusCodes502));
        var report = (MapMetadataRecomputeReport)objectResult.Value!;
        Assert.That(report.MmPatchError, Is.Not.Null);
        Assert.That(report.Maps.Single().Problem, Is.EqualTo(MapMetadataRecomputeProblems.MmPatchFailed));
    }

    private const int StatusCodes502 = 502;
}
