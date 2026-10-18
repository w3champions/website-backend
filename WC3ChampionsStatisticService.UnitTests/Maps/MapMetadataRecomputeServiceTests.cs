using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using W3C.Domain;
using W3C.Domain.MatchmakingService;
using W3C.Domain.UpdateService;
using W3ChampionsStatisticService.Maps;
using WC3ChampionsStatisticService.Tests.Admin;
using static WC3ChampionsStatisticService.Tests.Maps.RecomputeJson;

namespace WC3ChampionsStatisticService.Tests.Maps;

[TestFixture]
public class MapMetadataRecomputeServiceTests
{
    private const string NomadGamePath = @"maps\W3Champions\(3)NomadIsles_fix.w3x";
    private const string NomadFilePath = "W3Champions/(3)NomadIsles_fix.w3x";
    private static readonly string Sha1A = new('a', 40);
    private static readonly string Sha1B = new('b', 40);
    private static readonly string Sha1C = new('c', 40);
    private static readonly string Sha1D = new('d', 40);

    private static MapMetadataRecomputeService CreateService(RoutingHandler handler)
    {
        var factory = new RecordingHttpClientFactory(handler);
        return new MapMetadataRecomputeService(new MatchmakingServiceClient(factory), new UpdateServiceClient(factory));
    }

    private static RoutingHandler NomadChanged(bool dryRun) => new RoutingHandler()
        .On(HttpMethod.Get, RoutingHandler.MmMapsPath, HttpStatusCode.OK, MmMaps(MmMap(7, "Nomad Isles", NomadGamePath, Sha1A, 111, 222)))
        .On(HttpMethod.Post, RoutingHandler.UpdateRecomputePath, HttpStatusCode.OK,
            Recompute(dryRun, Result(NomadFilePath, Sha1A, "changed", 111, 333, 222, 444)))
        .On(HttpMethod.Post, RoutingHandler.MmGameMapMetadataPath, MmPatchEcho());

    [Test]
    public async Task DryRun_SendsMappedPathsWithTheAdminSecret_AndNeverPatchesMatchmaking()
    {
        var handler = NomadChanged(dryRun: true);

        var report = await CreateService(handler).Recompute(dryRun: true);

        var recomputeRequests = handler.RequestsFor(HttpMethod.Post, RoutingHandler.UpdateRecomputePath);
        Assert.That(recomputeRequests, Has.Count.EqualTo(1));
        Assert.That(recomputeRequests[0].Headers.GetValues(AdminSecretProvider.HeaderName), Is.EqualTo(new[] { AdminSecretTestEnvironment.Secret }));
        Assert.That(recomputeRequests[0].RequestUri!.Query, Is.Empty, "the secret must never be in the URL");
        var body = JObject.Parse(handler.BodiesFor(HttpMethod.Post, RoutingHandler.UpdateRecomputePath)[0]);
        Assert.That(body["dryRun"]!.Value<bool>(), Is.True);
        Assert.That(body["paths"]!.ToObject<string[]>(), Is.EqualTo(new[] { NomadFilePath }));

        Assert.That(handler.RequestsFor(HttpMethod.Post, RoutingHandler.MmGameMapMetadataPath), Is.Empty);

        Assert.That(report.DryRun, Is.True);
        var row = report.Maps.Single();
        Assert.That(row.MmMapId, Is.EqualTo(7));
        Assert.That(row.Name, Is.EqualTo("Nomad Isles"));
        Assert.That(row.Path, Is.EqualTo(NomadGamePath));
        Assert.That(row.UpdateServicePath, Is.EqualTo(NomadFilePath));
        Assert.That(row.Sha1, Is.EqualTo(Sha1A));
        Assert.That(row.MmChecksum, Is.EqualTo(111));
        Assert.That(row.OldChecksum, Is.EqualTo(111));
        Assert.That(row.NewChecksum, Is.EqualTo(333));
        Assert.That(row.NewCrc32, Is.EqualTo(444));
        Assert.That(row.Status, Is.EqualTo("changed"));
        Assert.That(row.MmPatchNeeded, Is.True);
        Assert.That(row.MmPatched, Is.False);
        Assert.That(row.Problem, Is.Null);
        Assert.That(report.Summary.Changed, Is.EqualTo(1));
        Assert.That(report.Summary.MmPatchNeeded, Is.EqualTo(1));
        Assert.That(report.Summary.MmPatchSent, Is.EqualTo(0));
    }

    [Test]
    public async Task NonDryRun_PatchesEachChangedSha1ExactlyOnceWithTheNewValues()
    {
        var handler = new RoutingHandler()
            .On(HttpMethod.Get, RoutingHandler.MmMapsPath, HttpStatusCode.OK, MmMaps(
                MmMap(1, "Shared one", @"maps\W3Champions\shared1.w3x", Sha1A, 1, 2),
                MmMap(2, "Shared two", @"maps\W3Champions\shared2.w3x", Sha1A, 1, 2),
                MmMap(3, "In sync", @"maps\W3Champions\sync.w3x", Sha1B, 5, 6),
                MmMap(4, "Other changed", @"maps\W3Champions\other.w3x", Sha1C, 7, 8)))
            .On(HttpMethod.Post, RoutingHandler.UpdateRecomputePath, HttpStatusCode.OK, Recompute(false,
                Result("W3Champions/shared1.w3x", Sha1A, "changed", 1, 10, 2, 20),
                Result("W3Champions/shared2.w3x", Sha1A, "changed", 1, 10, 2, 20),
                Result("W3Champions/sync.w3x", Sha1B, "unchanged", 5, 5, 6, 6),
                Result("W3Champions/other.w3x", Sha1C, "changed", 7, 70, 8, 80)))
            .On(HttpMethod.Post, RoutingHandler.MmGameMapMetadataPath, MmPatchEcho());

        var report = await CreateService(handler).Recompute(dryRun: false);

        var recomputeBody = JObject.Parse(handler.BodiesFor(HttpMethod.Post, RoutingHandler.UpdateRecomputePath)[0]);
        Assert.That(recomputeBody["dryRun"]!.Value<bool>(), Is.False);

        var patchRequests = handler.RequestsFor(HttpMethod.Post, RoutingHandler.MmGameMapMetadataPath);
        Assert.That(patchRequests, Has.Count.EqualTo(1));
        Assert.That(patchRequests[0].Headers.GetValues(AdminSecretProvider.HeaderName), Is.EqualTo(new[] { AdminSecretTestEnvironment.Secret }));
        var updates = (JArray)JObject.Parse(handler.BodiesFor(HttpMethod.Post, RoutingHandler.MmGameMapMetadataPath)[0])["updates"]!;
        Assert.That(updates.Select(u => (string)u["sha1"]), Is.EquivalentTo(new[] { Sha1A, Sha1C }));
        var a = updates.Single(u => (string)u["sha1"] == Sha1A);
        Assert.That((long)a["checksum"]!, Is.EqualTo(10));
        Assert.That((long)a["crc32"]!, Is.EqualTo(20));
        Assert.That(((JObject)a).Properties().Select(p => p.Name), Is.EquivalentTo(new[] { "sha1", "checksum", "crc32" }));

        Assert.That(report.Maps.Where(m => m.MmMapId != 3).All(m => m.MmPatched && m.MmMatched == 1 && m.MmModified == 1), Is.True);
        var inSync = report.Maps.Single(m => m.MmMapId == 3);
        Assert.That(inSync.MmPatchNeeded, Is.False);
        Assert.That(inSync.MmPatched, Is.False);
        Assert.That(report.Maps.All(m => m.Problem == null), Is.True);
        Assert.That(report.Summary.MmPatchSent, Is.EqualTo(2));
        Assert.That(report.Summary.Problems, Is.EqualTo(0));
        Assert.That(report.MmPatchError, Is.Null);
    }

    [Test]
    public async Task NonDryRun_AlsoPatchesAnUnchangedResultWhenMatchmakingStillHoldsTheOldValues()
    {
        // A rerun after update-service already stored the fix but matchmaking was not patched.
        var handler = new RoutingHandler()
            .On(HttpMethod.Get, RoutingHandler.MmMapsPath, HttpStatusCode.OK, MmMaps(MmMap(7, "Nomad Isles", NomadGamePath, Sha1A, 111, 222)))
            .On(HttpMethod.Post, RoutingHandler.UpdateRecomputePath, HttpStatusCode.OK,
                Recompute(false, Result(NomadFilePath, Sha1A, "unchanged", 333, 333, 444, 444)))
            .On(HttpMethod.Post, RoutingHandler.MmGameMapMetadataPath, MmPatchEcho());

        var report = await CreateService(handler).Recompute(dryRun: false);

        var updates = (JArray)JObject.Parse(handler.BodiesFor(HttpMethod.Post, RoutingHandler.MmGameMapMetadataPath).Single())["updates"]!;
        Assert.That((string)updates.Single()["sha1"], Is.EqualTo(Sha1A));
        Assert.That((long)updates.Single()["checksum"]!, Is.EqualTo(333));
        Assert.That(report.Maps.Single().MmPatched, Is.True);
    }

    [Test]
    public async Task NonDryRun_WithNothingToPatch_DoesNotCallMatchmaking()
    {
        var handler = new RoutingHandler()
            .On(HttpMethod.Get, RoutingHandler.MmMapsPath, HttpStatusCode.OK, MmMaps(MmMap(7, "Nomad Isles", NomadGamePath, Sha1A, 111, 222)))
            .On(HttpMethod.Post, RoutingHandler.UpdateRecomputePath, HttpStatusCode.OK,
                Recompute(false, Result(NomadFilePath, Sha1A, "unchanged", 111, 111, 222, 222)));

        var report = await CreateService(handler).Recompute(dryRun: false);

        Assert.That(handler.RequestsFor(HttpMethod.Post, RoutingHandler.MmGameMapMetadataPath), Is.Empty);
        Assert.That(report.Summary.Unchanged, Is.EqualTo(1));
        Assert.That(report.Summary.Problems, Is.EqualTo(0));
    }

    [Test]
    public async Task EveryFailureIsReportedAndNeverPatched()
    {
        var handler = new RoutingHandler()
            .On(HttpMethod.Get, RoutingHandler.MmMapsPath, HttpStatusCode.OK, MmMaps(
                MmMapWithoutGameMap(1, "No game map"),
                MmMap(2, "Missing", @"maps\W3Champions\gone.w3x", Sha1A, 1, 1),
                MmMap(3, "Broken", @"maps\W3Champions\broken.w3x", Sha1B, 1, 1),
                MmMap(4, "Other file", @"maps\W3Champions\replaced.w3x", Sha1C, 1, 1),
                MmMap(5, "No answer", @"maps\W3Champions\silent.w3x", Sha1D, 1, 1)))
            .On(HttpMethod.Post, RoutingHandler.UpdateRecomputePath, HttpStatusCode.OK, Recompute(false,
                Result("W3Champions/gone.w3x", null, "missing_file", error: "Map file not found"),
                Result("W3Champions/broken.w3x", Sha1B, "parse_error", 1, null, 1, null, "bad header"),
                Result("W3Champions/replaced.w3x", Sha1D.Replace('d', 'e'), "changed", 1, 9, 1, 9),
                Result("W3Champions/unknown.w3x", Sha1D.Replace('d', 'f'), "changed", 1, 9, 1, 9)))
            .On(HttpMethod.Post, RoutingHandler.MmGameMapMetadataPath, MmPatchEcho());

        var report = await CreateService(handler).Recompute(dryRun: false);

        var paths = JObject.Parse(handler.BodiesFor(HttpMethod.Post, RoutingHandler.UpdateRecomputePath)[0])["paths"]!.ToObject<string[]>();
        Assert.That(paths, Is.EquivalentTo(new[]
        {
            "W3Champions/gone.w3x", "W3Champions/broken.w3x", "W3Champions/replaced.w3x", "W3Champions/silent.w3x",
        }));

        Assert.That(report.Maps, Has.Count.EqualTo(5));
        Assert.That(report.Maps.Single(m => m.MmMapId == 1).Problem, Is.EqualTo(MapMetadataRecomputeProblems.NoPath));
        var missing = report.Maps.Single(m => m.MmMapId == 2);
        Assert.That(missing.Problem, Is.EqualTo("missing_file"));
        Assert.That(missing.Error, Is.EqualTo("Map file not found"));
        var broken = report.Maps.Single(m => m.MmMapId == 3);
        Assert.That(broken.Problem, Is.EqualTo("parse_error"));
        Assert.That(broken.Error, Is.EqualTo("bad header"));
        Assert.That(report.Maps.Single(m => m.MmMapId == 4).Problem, Is.EqualTo(MapMetadataRecomputeProblems.Sha1Differs));
        Assert.That(report.Maps.Single(m => m.MmMapId == 5).Problem, Is.EqualTo(MapMetadataRecomputeProblems.NoResult));
        Assert.That(report.Maps.Any(m => m.MmPatchNeeded || m.MmPatched), Is.False);

        Assert.That(report.UnmatchedResults.Select(r => r.Path), Is.EqualTo(new[] { "W3Champions/unknown.w3x" }));
        Assert.That(handler.RequestsFor(HttpMethod.Post, RoutingHandler.MmGameMapMetadataPath), Is.Empty);
        Assert.That(report.Summary.Problems, Is.EqualTo(5));
        Assert.That(report.Summary.UnmatchedResults, Is.EqualTo(1));
    }

    [Test]
    public async Task ChangedResultWithAWriteError_IsNotPatchedAndIsReported()
    {
        var handler = new RoutingHandler()
            .On(HttpMethod.Get, RoutingHandler.MmMapsPath, HttpStatusCode.OK, MmMaps(
                MmMap(7, "Nomad Isles", NomadGamePath, Sha1A, 111, 222),
                MmMap(8, "Written", @"maps\W3Champions\ok.w3x", Sha1B, 1, 2)))
            .On(HttpMethod.Post, RoutingHandler.UpdateRecomputePath, HttpStatusCode.OK, Recompute(false,
                Result(NomadFilePath, Sha1A, "changed", 111, 333, 222, 444, "write failed: timeout"),
                Result("W3Champions/ok.w3x", Sha1B, "changed", 1, 3, 2, 4)))
            .On(HttpMethod.Post, RoutingHandler.MmGameMapMetadataPath, MmPatchEcho());

        var report = await CreateService(handler).Recompute(dryRun: false);

        var updates = (JArray)JObject.Parse(handler.BodiesFor(HttpMethod.Post, RoutingHandler.MmGameMapMetadataPath).Single())["updates"]!;
        Assert.That(updates.Select(u => (string)u["sha1"]), Is.EqualTo(new[] { Sha1B }));
        var failed = report.Maps.Single(m => m.MmMapId == 7);
        Assert.That(failed.Problem, Is.EqualTo(MapMetadataRecomputeProblems.UpdateServiceWriteFailed));
        Assert.That(failed.Error, Is.EqualTo("write failed: timeout"));
        Assert.That(failed.MmPatchNeeded, Is.False);
        Assert.That(failed.MmPatched, Is.False);
        Assert.That(report.Maps.Single(m => m.MmMapId == 8).MmPatched, Is.True);
        Assert.That(report.Summary.Problems, Is.EqualTo(1));
    }

    [Test]
    public async Task WriteFailedResult_IsNotPatchedAndIsReported()
    {
        var handler = NomadChanged(dryRun: false)
            .On(HttpMethod.Post, RoutingHandler.UpdateRecomputePath, HttpStatusCode.OK,
                Recompute(false, Result(NomadFilePath, Sha1A, "write_failed", 111, 333, 222, 444, "write failed: timeout")));

        var report = await CreateService(handler).Recompute(dryRun: false);

        Assert.That(handler.RequestsFor(HttpMethod.Post, RoutingHandler.MmGameMapMetadataPath), Is.Empty);
        var row = report.Maps.Single();
        Assert.That(row.Problem, Is.EqualTo("write_failed"));
        Assert.That(row.Error, Is.EqualTo("write failed: timeout"));
        Assert.That(row.MmPatchNeeded, Is.False);
        Assert.That(report.Summary.Problems, Is.EqualTo(1));
    }

    [TestCase(-1L)]
    [TestCase(4294967296L)]
    public async Task NewValuesOutsideUint32_AreNotSentToMatchmaking(long checksum)
    {
        var handler = NomadChanged(dryRun: false)
            .On(HttpMethod.Post, RoutingHandler.UpdateRecomputePath, HttpStatusCode.OK,
                Recompute(false, Result(NomadFilePath, Sha1A, "changed", 111, checksum, 222, 444)));

        var report = await CreateService(handler).Recompute(dryRun: false);

        Assert.That(handler.RequestsFor(HttpMethod.Post, RoutingHandler.MmGameMapMetadataPath), Is.Empty);
        Assert.That(report.Maps.Single().Problem, Is.EqualTo(MapMetadataRecomputeProblems.InvalidNewValues));
    }

    private static (RoutingHandler Handler, int Count) ManyMaps(int count, Func<int, string, HttpResponseMessage> recompute = null)
    {
        var maps = Enumerable.Range(1, count).Select(i => MmMap(i, $"m{i}", $@"maps\W3Champions\m{i}.w3x", Sha1Of(i), 1, 2)).ToArray();
        var call = 0;
        var handler = new RoutingHandler()
            .On(HttpMethod.Get, RoutingHandler.MmMapsPath, HttpStatusCode.OK, MmMaps(maps))
            .On(HttpMethod.Post, RoutingHandler.UpdateRecomputePath, body =>
            {
                call++;
                var custom = recompute?.Invoke(call, body);
                if (custom != null) return custom;
                var results = JObject.Parse(body)["paths"]!.Select(p => (string)p)
                    .Select(p => Result(p, Sha1Of(int.Parse(p["W3Champions/m".Length..^".w3x".Length])), "changed", 1, 3, 2, 4))
                    .ToArray();
                return RoutingHandler.Json(HttpStatusCode.OK, Recompute(false, results));
            })
            .On(HttpMethod.Post, RoutingHandler.MmGameMapMetadataPath, MmPatchEcho());
        return (handler, count);
    }

    private static string Sha1Of(int i) => i.ToString("x40");

    [Test]
    public async Task UpdateServiceIsCalledInSequentialChunksAndResultsAreAggregated()
    {
        var (handler, _) = ManyMaps(120);

        var report = await CreateService(handler).Recompute(dryRun: false);

        var chunks = handler.BodiesFor(HttpMethod.Post, RoutingHandler.UpdateRecomputePath)
            .Select(b => JObject.Parse(b)["paths"]!.Count()).ToList();
        Assert.That(chunks, Is.EqualTo(new[] { 50, 50, 20 }));
        Assert.That(MapMetadataRecomputeService.UpdateServicePathsPerRequest, Is.EqualTo(50));
        Assert.That(report.Maps.All(m => m.MmPatched), Is.True);
        Assert.That(report.Summary.PathsSent, Is.EqualTo(120));
        Assert.That(report.Summary.Problems, Is.EqualTo(0));
    }

    [Test]
    public void AFailedUpdateServiceChunk_AbortsBeforeAnyMatchmakingPatch()
    {
        var (handler, _) = ManyMaps(120, (call, _) =>
            call == 2 ? RoutingHandler.Json(HttpStatusCode.InternalServerError, "{\"message\":\"boom\"}") : null);

        var ex = Assert.ThrowsAsync<MapMetadataRecomputeUpstreamException>(() => CreateService(handler).Recompute(dryRun: false));

        Assert.That(handler.RequestsFor(HttpMethod.Post, RoutingHandler.UpdateRecomputePath), Has.Count.EqualTo(2), "later chunks are not sent");
        Assert.That(handler.RequestsFor(HttpMethod.Post, RoutingHandler.MmGameMapMetadataPath), Is.Empty);
        Assert.That(ex!.PathsCompleted, Is.EqualTo(50));
        Assert.That(ex.PathsTotal, Is.EqualTo(120));
        Assert.That(ex.UpstreamStatus, Is.EqualTo(500));
        Assert.That(ex.Message, Does.Contain("Matchmaking was not patched"));
    }

    [Test]
    public async Task MatchmakingPatchesAreChunkedAtTheMatchmakingLimit_AndAFailedChunkOnlyFailsItsRows()
    {
        var (handler, count) = ManyMaps(MapMetadataRecomputeService.MatchmakingUpdatesPerRequest + 1);
        var mmCall = 0;
        handler.On(HttpMethod.Post, RoutingHandler.MmGameMapMetadataPath, body =>
            ++mmCall == 1 ? MmPatchEcho()(body) : RoutingHandler.Json(HttpStatusCode.InternalServerError, "{}"));

        var report = await CreateService(handler).Recompute(dryRun: false);

        var sizes = handler.BodiesFor(HttpMethod.Post, RoutingHandler.MmGameMapMetadataPath)
            .Select(b => JObject.Parse(b)["updates"]!.Count()).ToList();
        Assert.That(sizes, Is.EqualTo(new[] { 2000, 1 }));
        Assert.That(report.Maps.Count(m => m.MmPatched), Is.EqualTo(2000));
        Assert.That(report.Maps.Single(m => m.Problem != null).Problem, Is.EqualTo(MapMetadataRecomputeProblems.MmPatchFailed));
        Assert.That(report.MmPatchError, Is.Not.Null);
        Assert.That(report.Maps, Has.Count.EqualTo(count));
        Assert.That(report.Summary.MmPatchSent, Is.EqualTo(count), "sha1s of the failed chunk were sent too");
        Assert.That(report.Summary.MmMatched, Is.EqualTo(2000));
    }

    [Test]
    public async Task MapIds_LimitTheRunToThoseMaps_AndUnknownIdsAreReported()
    {
        var handler = new RoutingHandler()
            .On(HttpMethod.Get, RoutingHandler.MmMapsPath, HttpStatusCode.OK, MmMaps(
                MmMap(5241, "Nomad Isles", NomadGamePath, Sha1A, 111, 222),
                MmMap(8, "Other", @"maps\W3Champions\other.w3x", Sha1B, 1, 2)))
            .On(HttpMethod.Post, RoutingHandler.UpdateRecomputePath, HttpStatusCode.OK,
                Recompute(false, Result(NomadFilePath, Sha1A, "changed", 111, 333, 222, 444)))
            .On(HttpMethod.Post, RoutingHandler.MmGameMapMetadataPath, MmPatchEcho());

        var report = await CreateService(handler).Recompute(dryRun: false, mapIds: [5241, 999]);

        var paths = JObject.Parse(handler.BodiesFor(HttpMethod.Post, RoutingHandler.UpdateRecomputePath).Single())["paths"]!.ToObject<string[]>();
        Assert.That(paths, Is.EqualTo(new[] { NomadFilePath }));
        Assert.That(report.Maps.Select(m => m.MmMapId), Is.EqualTo(new[] { 5241 }));
        Assert.That(report.Maps.Single().MmPatched, Is.True);
        Assert.That(report.UnknownMapIds, Is.EqualTo(new[] { 999 }));
        Assert.That(report.Summary.UnknownMapIds, Is.EqualTo(1));
        Assert.That(report.Summary.MmMaps, Is.EqualTo(1));
    }

    [Test]
    public async Task WithoutMapIds_EveryMapIsProcessedAndNoIdIsUnknown()
    {
        var report = await CreateService(NomadChanged(dryRun: true)).Recompute(dryRun: true, mapIds: null);

        Assert.That(report.Maps, Has.Count.EqualTo(1));
        Assert.That(report.UnknownMapIds, Is.Empty);
    }

    [Test]
    public async Task MatchmakingSha1ThatIsNotLowercaseHex_IsReportedAndNotPatched()
    {
        var handler = new RoutingHandler()
            .On(HttpMethod.Get, RoutingHandler.MmMapsPath, HttpStatusCode.OK, MmMaps(MmMap(7, "Nomad Isles", NomadGamePath, Sha1A.ToUpperInvariant(), 111, 222)))
            .On(HttpMethod.Post, RoutingHandler.UpdateRecomputePath, HttpStatusCode.OK,
                Recompute(false, Result(NomadFilePath, Sha1A, "changed", 111, 333, 222, 444)))
            .On(HttpMethod.Post, RoutingHandler.MmGameMapMetadataPath, MmPatchEcho());

        var report = await CreateService(handler).Recompute(dryRun: false);

        Assert.That(handler.RequestsFor(HttpMethod.Post, RoutingHandler.MmGameMapMetadataPath), Is.Empty);
        var row = report.Maps.Single();
        Assert.That(row.Problem, Is.EqualTo(MapMetadataRecomputeProblems.MmSha1NotLowercaseHex));
        Assert.That(row.NewChecksum, Is.EqualTo(333), "the recomputed values are still shown");
    }

    [Test]
    public async Task PathsDifferingOnlyInCase_AreSentOnce()
    {
        var handler = new RoutingHandler()
            .On(HttpMethod.Get, RoutingHandler.MmMapsPath, HttpStatusCode.OK, MmMaps(
                MmMap(1, "a", @"maps\W3Champions\X.w3x", Sha1A, 1, 2),
                MmMap(2, "b", @"maps\W3Champions\x.w3x", Sha1A, 1, 2)))
            .On(HttpMethod.Post, RoutingHandler.UpdateRecomputePath, HttpStatusCode.OK,
                Recompute(true, Result("W3Champions/X.w3x", Sha1A, "changed", 1, 3, 2, 4)));

        var report = await CreateService(handler).Recompute(dryRun: true);

        var paths = JObject.Parse(handler.BodiesFor(HttpMethod.Post, RoutingHandler.UpdateRecomputePath).Single())["paths"]!.ToObject<string[]>();
        Assert.That(paths, Is.EqualTo(new[] { "W3Champions/X.w3x" }));
        Assert.That(report.Summary.PathsSent, Is.EqualTo(1));
        Assert.That(report.Maps.All(m => m.Problem == null), Is.True);
    }

    [Test]
    public async Task MatchmakingNotMatchingAPatchedSha1_IsReported()
    {
        var handler = NomadChanged(dryRun: false)
            .On(HttpMethod.Post, RoutingHandler.MmGameMapMetadataPath, MmPatchEcho(matched: 0, modified: 0));

        var report = await CreateService(handler).Recompute(dryRun: false);

        var row = report.Maps.Single();
        Assert.That(row.MmPatched, Is.False);
        Assert.That(row.MmMatched, Is.EqualTo(0));
        Assert.That(row.Problem, Is.EqualTo(MapMetadataRecomputeProblems.MmNotMatched));
        Assert.That(report.Summary.Problems, Is.EqualTo(1));
    }

    [Test]
    public async Task MatchmakingPatchFailure_IsReportedOnEveryRowItConcerned()
    {
        var handler = NomadChanged(dryRun: false)
            .On(HttpMethod.Post, RoutingHandler.MmGameMapMetadataPath, HttpStatusCode.BadRequest,
                "{\"errors\":[{\"param\":\"updates\",\"msg\":\"too many\"}]}");

        var report = await CreateService(handler).Recompute(dryRun: false);

        Assert.That(report.MmPatchError, Is.Not.Null.And.Not.Empty);
        var row = report.Maps.Single();
        Assert.That(row.MmPatched, Is.False);
        Assert.That(row.Problem, Is.EqualTo(MapMetadataRecomputeProblems.MmPatchFailed));
        Assert.That(report.Summary.Problems, Is.EqualTo(1));
    }

    [Test]
    public void UpdateServiceErrorStatus_FailsWithoutPatchingMatchmaking()
    {
        var handler = NomadChanged(dryRun: false)
            .On(HttpMethod.Post, RoutingHandler.UpdateRecomputePath, HttpStatusCode.InternalServerError, "{\"message\":\"boom\"}");

        var ex = Assert.ThrowsAsync<MapMetadataRecomputeUpstreamException>(() => CreateService(handler).Recompute(dryRun: false));

        Assert.That(ex!.Service, Is.EqualTo(MapMetadataRecomputeUpstreamException.UpdateService));
        Assert.That(ex.UpstreamStatus, Is.EqualTo(500));
        Assert.That(handler.RequestsFor(HttpMethod.Post, RoutingHandler.MmGameMapMetadataPath), Is.Empty);
    }

    [TestCase("{\"message\":\"Unauthorized\"}")]
    [TestCase("<html>bad gateway</html>")]
    [TestCase("")]
    public void UpdateServiceErrorBodiesOfAnyShape_FailTheRequest(string body)
    {
        var handler = NomadChanged(dryRun: false)
            .On(HttpMethod.Post, RoutingHandler.UpdateRecomputePath, HttpStatusCode.Unauthorized, body);

        var ex = Assert.ThrowsAsync<MapMetadataRecomputeUpstreamException>(() => CreateService(handler).Recompute(dryRun: false));

        Assert.That(ex!.UpstreamStatus, Is.EqualTo(401));
        Assert.That(handler.RequestsFor(HttpMethod.Post, RoutingHandler.MmGameMapMetadataPath), Is.Empty);
    }

    [Test]
    public void UpdateServiceUnreachableOrTimingOut_FailsWithoutPatchingMatchmaking()
    {
        foreach (var exception in new Exception[] { new HttpRequestException("connection refused"), new TaskCanceledException("timeout") })
        {
            var handler = NomadChanged(dryRun: false).OnThrow(HttpMethod.Post, RoutingHandler.UpdateRecomputePath, exception);

            var ex = Assert.ThrowsAsync<MapMetadataRecomputeUpstreamException>(() => CreateService(handler).Recompute(dryRun: false));

            Assert.That(ex!.UpstreamStatus, Is.Null);
            Assert.That(handler.RequestsFor(HttpMethod.Post, RoutingHandler.MmGameMapMetadataPath), Is.Empty);
        }
    }

    [Test]
    public void UpdateServiceAnsweringWithoutResults_FailsTheRequest()
    {
        var handler = NomadChanged(dryRun: false)
            .On(HttpMethod.Post, RoutingHandler.UpdateRecomputePath, HttpStatusCode.OK, "{\"dryRun\":false}");

        Assert.ThrowsAsync<MapMetadataRecomputeUpstreamException>(() => CreateService(handler).Recompute(dryRun: false));
        Assert.That(handler.RequestsFor(HttpMethod.Post, RoutingHandler.MmGameMapMetadataPath), Is.Empty);
    }

    [Test]
    public void MatchmakingMapListFailure_FailsBeforeCallingUpdateService()
    {
        var handler = NomadChanged(dryRun: false)
            .On(HttpMethod.Get, RoutingHandler.MmMapsPath, HttpStatusCode.InternalServerError, "{\"error\":\"down\"}");

        var ex = Assert.ThrowsAsync<MapMetadataRecomputeUpstreamException>(() => CreateService(handler).Recompute(dryRun: false));

        Assert.That(ex!.Service, Is.EqualTo(MapMetadataRecomputeUpstreamException.Matchmaking));
        Assert.That(handler.RequestsFor(HttpMethod.Post, RoutingHandler.UpdateRecomputePath), Is.Empty);
    }

    [Test]
    public async Task NoMapWithAPath_SkipsUpdateServiceInsteadOfAskingForEveryFile()
    {
        var handler = new RoutingHandler()
            .On(HttpMethod.Get, RoutingHandler.MmMapsPath, HttpStatusCode.OK, MmMaps(MmMapWithoutGameMap(1, "No game map")));

        var report = await CreateService(handler).Recompute(dryRun: false);

        Assert.That(handler.RequestsFor(HttpMethod.Post, RoutingHandler.UpdateRecomputePath), Is.Empty);
        Assert.That(report.Maps.Single().Problem, Is.EqualTo(MapMetadataRecomputeProblems.NoPath));
    }
}
