using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using Microsoft.AspNetCore.WebUtilities;
using W3ChampionsStatisticService.Maps;

namespace WC3ChampionsStatisticService.Tests.Maps;

/// <summary>
/// What every <see cref="TemporaryMapExpirySweep"/> fixture shares: the clock, the TTL boundary it implies, the scripted
/// routes and the responders that build matchmaking's and update-service's pages. The sweep runs over the real clients
/// and one scripted handler; the clock is the parameter, so nothing here waits. Every sweep purges the per-test spool
/// directory of the base, never the machine-wide one (<see cref="TemporaryMapExpirySweepSpoolTests"/> covers that pass).
/// </summary>
public abstract class TemporaryMapExpirySweepTestBase : TemporaryMapUploadServiceTestBase
{
    protected static readonly DateTime Now = new(2026, 9, 14, 3, 0, 0, DateTimeKind.Utc);

    /// <summary>The TTL boundary the sweep sends as <c>before</c>: a record hosted at or after it is not expired.</summary>
    protected static readonly long Before = new DateTimeOffset(Now.AddDays(-TemporaryMapLimits.TtlDays)).ToUnixTimeMilliseconds();
    protected const string FileA = "W3Champions/CustomGames/a-11111111.w3x";
    protected const string FileB = "W3Champions/CustomGames/b-22222222.w3x";
    protected const string FileC = "W3Champions/CustomGames/c-33333333.w3x";
    protected const string Expired = "/maps/temporary/expired";
    protected const string Listing = "/api/content/maps/files";
    protected const string ByPath = "/maps/temporary/by-path";
    protected const string UsFile = "/api/content/maps/file?";
    protected const string FileDeleted = "/file-deleted";

    // ---- Helpers -----------------------------------------------------------------------------

    protected static bool IsExpired(HttpRequestMessage r)
        => r.Method == HttpMethod.Get && r.RequestUri!.PathAndQuery.Contains(Expired, StringComparison.Ordinal);

    protected static bool IsListing(HttpRequestMessage r)
        => r.Method == HttpMethod.Get && r.RequestUri!.PathAndQuery.Contains(Listing, StringComparison.Ordinal);

    protected static bool IsByPathOf(HttpRequestMessage r, string fileNamePart)
        => r.Method == HttpMethod.Get && r.RequestUri!.PathAndQuery.Contains(ByPath, StringComparison.Ordinal)
           && r.RequestUri.Query.Contains(fileNamePart, StringComparison.Ordinal);

    protected static bool IsByPathProbe(HttpRequestMessage r)
        => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.EndsWith(ByPath, StringComparison.Ordinal);

    /// <summary>The decoded <c>path</c> the by-path probe asked about.</summary>
    protected static string PathQueryOf(HttpRequestMessage r) => QueryHelpers.ParseQuery(r.RequestUri!.Query)["path"].ToString();

    protected static bool IsByPathFor(HttpRequestMessage r, string path)
        => IsByPathProbe(r) && string.Equals(PathQueryOf(r), path, StringComparison.Ordinal);

    /// <summary>
    /// The by-path answer that confirms an expiry item: the record of that id, present at that very path, last hosted
    /// before the TTL boundary (<paramref name="lastHostedAt"/> defaults to one millisecond before it).
    /// </summary>
    protected static string Claim(int id, string path, string fileState = "present", long? lastHostedAt = long.MinValue)
    {
        var hosted = lastHostedAt == long.MinValue ? Before - 1 : lastHostedAt;
        return "{\"map\":{\"id\":" + id + ",\"name\":\"m\",\"path\":\"" + path + "\",\"temporary\":true,\"fileState\":\"" + fileState + "\"" +
               (hosted == null ? "" : ",\"lastHostedAt\":" + hosted) + "}}";
    }

    /// <summary>One by-path responder confirming each of <paramref name="items"/> as expired; any other path is unclaimed.</summary>
    protected static Func<HttpRequestMessage, HttpResponseMessage> ClaimsOf(params (int Id, string Path)[] items)
    {
        var byPath = items.ToDictionary(i => i.Path, i => i.Id, StringComparer.Ordinal);
        return r =>
        {
            var path = PathQueryOf(r);
            return byPath.TryGetValue(path, out var id)
                ? ScriptedHttpHandler.Json(HttpStatusCode.OK, Claim(id, path))
                : ScriptedHttpHandler.Json(HttpStatusCode.NotFound, "{}");
        };
    }

    /// <summary>
    /// The one-row re-probe a reclaim sends after its delete, told apart from a page listing by its limit: only the
    /// probe asks for a single row.
    /// </summary>
    protected static bool IsReclaimProbe(HttpRequestMessage r)
        => IsListing(r) && QueryHelpers.ParseQuery(r.RequestUri!.Query)["limit"].ToString() == "1";

    /// <summary>The decoded <c>after</c> cursor a listing request sent, or null when it sent none.</summary>
    protected static string AfterOf(HttpRequestMessage r)
        => QueryHelpers.ParseQuery(r.RequestUri!.Query).TryGetValue("after", out var after) ? after.ToString() : null;

    /// <summary>The reclaim probe's answer when the delete really removed the file: no row between the bound and it.</summary>
    protected static Func<HttpRequestMessage, HttpResponseMessage> Gone()
        => _ => ScriptedHttpHandler.Json(HttpStatusCode.OK, Files(null));

    /// <summary>The reclaim probe's answer when update-service kept the file: the very path, still the first row after the bound.</summary>
    protected static Func<HttpRequestMessage, HttpResponseMessage> StillStored(string filePath)
        => _ => ScriptedHttpHandler.Json(HttpStatusCode.OK, Files(null, filePath));

    protected static string SweepRoute(HttpRequestMessage r)
        => IsExpired(r) ? "expired" : IsListing(r) ? "files" : r.RequestUri!.AbsolutePath;

    protected static (int Id, string Path)[] Range(int firstId, int count)
        => Enumerable.Range(firstId, count).Select(id => (id, $"W3Champions/CustomGames/m-{id:D8}.w3x")).ToArray();

    /// <summary>The GET /maps/temporary/expired page holding these records.</summary>
    protected static string Items(params (int Id, string Path)[] items)
        => "{\"items\":[" + string.Join(",", items.Select(i => "{\"id\":" + i.Id + ",\"path\":\"" + i.Path + "\"}")) + "]}";

    /// <summary>The GET /api/content/maps/files page holding these files and cursor.</summary>
    protected static string Files(string next, params string[] filePaths)
    {
        var files = string.Join(",", filePaths.Select(p => "{\"filePath\":\"" + p + "\",\"sizeBytes\":1,\"modifiedAt\":\"2026-09-01T00:00:00Z\"}"));
        return "{\"files\":[" + files + "],\"next\":" + (next == null ? "null" : "\"" + next + "\"") + "}";
    }

    private protected static ScriptedHttpHandler EmptyReconciliation()
        => new ScriptedHttpHandler().On(HttpMethod.Get, Listing, HttpStatusCode.OK, Files(null));

    /// <summary>
    /// A handler whose reclaim probes all answer "gone", the ordinary case: registered first, so it is matched before
    /// the fixture's own page listing.
    /// </summary>
    private protected static ScriptedHttpHandler ReclaimsAreVerified()
        => new ScriptedHttpHandler().On(IsReclaimProbe, Gone());

    /// <summary>
    /// One 200 page per call, in order, and a failure once they run out: a loop that stops where it should never asks
    /// for more, and one that does not stop fails the test instead of spinning on a route that always answers.
    /// </summary>
    protected static Func<HttpRequestMessage, HttpResponseMessage> Sequence(params string[] pages)
    {
        var remaining = new Queue<string>(pages);
        return _ => remaining.Count > 0
            ? ScriptedHttpHandler.Json(HttpStatusCode.OK, remaining.Dequeue())
            : throw new InvalidOperationException("the sweep asked for more pages than the test scripted");
    }
}
