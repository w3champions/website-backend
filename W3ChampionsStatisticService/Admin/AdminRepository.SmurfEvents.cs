using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace W3ChampionsStatisticService.Admin;

/// <summary>
/// Proxy for the matchmaking smurf events moderation API (/admin/smurf-detection/events). Shares the client,
/// secret and error handling of the smurf-detection proxy (AdminRepository.SmurfDetection.cs); the author of
/// every write is supplied from the acting moderator's token.
/// </summary>
public partial class AdminRepository
{
    private const string SmurfEventsPath = "/events";

    public Task<MatchmakingProxyResult> GetSmurfEvents(string review, string origin, string cursor, int? limit)
    {
        var query = new List<string>();
        if (!string.IsNullOrEmpty(review)) query.Add($"review={Segment(review)}");
        if (!string.IsNullOrEmpty(origin)) query.Add($"origin={Segment(origin)}");
        if (!string.IsNullOrEmpty(cursor)) query.Add($"cursor={Segment(cursor)}");
        if (limit.HasValue) query.Add($"limit={limit.Value}");
        return SendSmurfEvents(HttpMethod.Get, query.Count == 0 ? "" : "?" + string.Join('&', query));
    }

    public Task<MatchmakingProxyResult> GetSmurfEvent(string id) =>
        SendSmurfEvents(HttpMethod.Get, $"/{Segment(id)}");

    public Task<MatchmakingProxyResult> GetSmurfEventDenyPreview(string id) =>
        SendSmurfEvents(HttpMethod.Get, $"/{Segment(id)}/deny-preview");

    public Task<MatchmakingProxyResult> PreviewSmurfEvent(string name, string from, string to, IReadOnlyList<string> anchors, string replacing) =>
        SendSmurfEvents(HttpMethod.Post, "/preview", EventBody(("name", name), ("from", from), ("to", to), ("anchors", anchors), ("replacing", replacing)));

    public Task<MatchmakingProxyResult> CreateSmurfEvent(string name, string from, string to, IReadOnlyList<string> anchors, string reason, string author) =>
        SendSmurfEvents(HttpMethod.Post, "", EventBody(("name", name), ("from", from), ("to", to), ("anchors", anchors), ("reason", reason), ("author", author)));

    public Task<MatchmakingProxyResult> UpdateSmurfEvent(string id, string name, string from, string to, IReadOnlyList<string> anchors, string reason, string author) =>
        SendSmurfEvents(HttpMethod.Put, $"/{Segment(id)}", EventBody(("name", name), ("from", from), ("to", to), ("anchors", anchors), ("reason", reason), ("author", author)));

    public Task<MatchmakingProxyResult> DeleteSmurfEvent(string id, string reason, string author) =>
        SendSmurfEvents(HttpMethod.Delete, $"/{Segment(id)}", EventBody(("reason", reason), ("author", author)));

    public Task<MatchmakingProxyResult> ReviewSmurfEvent(string id, string decision, string reason, string author) =>
        SendSmurfEvents(HttpMethod.Post, $"/{Segment(id)}/review", EventBody(("decision", decision), ("reason", reason), ("author", author)));

    private Task<MatchmakingProxyResult> SendSmurfEvents(HttpMethod method, string path, object body = null) =>
        SendSmurfDetection(method, SmurfEventsPath + path, body);

    // Absent optional fields are omitted rather than sent as null.
    private static Dictionary<string, object> EventBody(params (string Key, object Value)[] fields)
    {
        var body = new Dictionary<string, object>();
        foreach (var (key, value) in fields)
            if (value != null) body[key] = value;
        return body;
    }
}
