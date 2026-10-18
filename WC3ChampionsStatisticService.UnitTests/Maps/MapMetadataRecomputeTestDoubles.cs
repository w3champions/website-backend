using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WC3ChampionsStatisticService.Tests.Maps;

/// <summary>
/// Answers matchmaking and update-service calls by method + path, and records every request with its
/// body. Unconfigured routes fail the test loudly instead of returning an empty 200.
/// </summary>
internal sealed class RoutingHandler : HttpMessageHandler
{
    public const string MmMapsPath = "/maps";
    public const string MmGameMapMetadataPath = "/maps/game-map-metadata";
    public const string UpdateRecomputePath = "/api/content/maps/metadata/recompute";

    private readonly Dictionary<(HttpMethod, string), Func<string, HttpResponseMessage>> _routes = [];

    public List<(HttpRequestMessage Request, string Body)> Calls { get; } = [];

    public RoutingHandler On(HttpMethod method, string path, HttpStatusCode status, string body) =>
        On(method, path, _ => Json(status, body));

    public RoutingHandler On(HttpMethod method, string path, Func<string, HttpResponseMessage> respond)
    {
        _routes[(method, path)] = respond;
        return this;
    }

    public RoutingHandler OnThrow(HttpMethod method, string path, Exception exception) =>
        On(method, path, _ => throw exception);

    public List<string> BodiesFor(HttpMethod method, string path) =>
        Calls.Where(c => c.Request.Method == method && c.Request.RequestUri!.AbsolutePath == path).Select(c => c.Body).ToList();

    public List<HttpRequestMessage> RequestsFor(HttpMethod method, string path) =>
        Calls.Where(c => c.Request.Method == method && c.Request.RequestUri!.AbsolutePath == path).Select(c => c.Request).ToList();

    public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Calls.Add((request, body));

        if (!_routes.TryGetValue((request.Method, request.RequestUri!.AbsolutePath), out var respond))
        {
            throw new InvalidOperationException($"Unexpected request {request.Method} {request.RequestUri}");
        }

        return respond(body);
    }
}

/// <summary>Builders for the JSON the sibling services answer with.</summary>
internal static class RecomputeJson
{
    public static string MmMaps(params object[] maps) =>
        JsonConvert.SerializeObject(new { total = maps.Length, items = maps });

    public static object MmMap(int id, string name, string path, string sha1, long checksum, long crc32) =>
        new { id, name, gameMap = new { path, sha1, checksum, crc32, name } };

    public static object MmMapWithoutGameMap(int id, string name) => new { id, name };

    public static string Recompute(bool dryRun, params object[] results) =>
        JsonConvert.SerializeObject(new { dryRun, results });

    public static object Result(
        string path,
        string sha1,
        string status,
        long? oldChecksum = null,
        long? newChecksum = null,
        long? oldCrc32 = null,
        long? newCrc32 = null,
        string error = null) =>
        new
        {
            path,
            mapDataId = sha1 == null ? null : "md-" + path,
            sha1,
            oldChecksum,
            newChecksum,
            oldCrc32,
            newCrc32,
            changed = status == "changed",
            status,
            error,
        };

    /// <summary>Answers each requested update with the given matched/modified counts.</summary>
    public static Func<string, HttpResponseMessage> MmPatchEcho(int matched = 1, int modified = 1) =>
        MmPatchEcho(_ => matched, modified);

    /// <summary>Answers each requested update with a per-sha1 matched count (as many maps as carry it), modified = matched.</summary>
    public static Func<string, HttpResponseMessage> MmPatchEcho(Func<string, int> matchedFor) => MmPatchEcho(matchedFor, null);

    private static Func<string, HttpResponseMessage> MmPatchEcho(Func<string, int> matchedFor, int? modified) => body =>
    {
        var updates = (JArray)JObject.Parse(body)["updates"]!;
        var results = updates.Select(u => (string)u["sha1"])
            .Select(sha1 => new { sha1, matched = matchedFor(sha1), modified = modified ?? matchedFor(sha1) });
        return RoutingHandler.Json(HttpStatusCode.OK, JsonConvert.SerializeObject(new { results }));
    };
}
