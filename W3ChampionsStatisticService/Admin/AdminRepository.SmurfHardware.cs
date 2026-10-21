using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Serilog;

namespace W3ChampionsStatisticService.Admin;

/// <summary>
/// Proxy for the matchmaking hardware smurf moderation API (/admin/smurf-detection). Responses are forwarded
/// unchanged; the author of every write is supplied by the caller from the acting moderator's token.
/// </summary>
public partial class AdminRepository
{
    private const string SmurfDetectionPath = "/admin/smurf-detection";

    public Task<MatchmakingProxyResult> GetHardwareLinks(string battleTag) =>
        SendSmurfHardware(HttpMethod.Get, $"/hardware/links/{Segment(battleTag)}");

    public Task<MatchmakingProxyResult> GetHardwareItems(string battleTag) =>
        SendSmurfHardware(HttpMethod.Get, $"/hardware/items/{Segment(battleTag)}");

    public Task<MatchmakingProxyResult> GetHardwareItem(string battleTag, string hash8) =>
        SendSmurfHardware(HttpMethod.Get, ItemPath(battleTag, hash8));

    public Task<MatchmakingProxyResult> GetHardwareAudit(string battleTag, string before, int? limit)
    {
        var query = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrEmpty(battleTag)) query.Add($"battleTag={Segment(battleTag)}");
        if (!string.IsNullOrEmpty(before)) query.Add($"before={Segment(before)}");
        if (limit.HasValue) query.Add($"limit={limit.Value}");
        return SendSmurfHardware(HttpMethod.Get, "/hardware/audit" + (query.Count == 0 ? "" : "?" + string.Join('&', query)));
    }

    public Task<MatchmakingProxyResult> SetSmurfVerdict(string battleTagA, string battleTagB, string reason, string author) =>
        SendSmurfHardware(HttpMethod.Post, PairPath(battleTagA, battleTagB, "smurf"), new { reason, author });

    public Task<MatchmakingProxyResult> RevokeSmurfVerdict(string battleTagA, string battleTagB, string author) =>
        SendSmurfHardware(HttpMethod.Delete, PairPath(battleTagA, battleTagB, "smurf"), new { author });

    public Task<MatchmakingProxyResult> SetNotSmurfVerdict(string battleTagA, string battleTagB, string reason, string author) =>
        SendSmurfHardware(HttpMethod.Post, PairPath(battleTagA, battleTagB, "not-smurf"), new { reason, author });

    public Task<MatchmakingProxyResult> RevokeNotSmurfVerdict(string battleTagA, string battleTagB, string author) =>
        SendSmurfHardware(HttpMethod.Delete, PairPath(battleTagA, battleTagB, "not-smurf"), new { author });

    public Task<MatchmakingProxyResult> TrustHardwareItem(string battleTag, string hash8, string reason, string author) =>
        SendSmurfHardware(HttpMethod.Post, ItemPath(battleTag, hash8) + "/trust", new { reason, author });

    public Task<MatchmakingProxyResult> RevokeHardwareItemTrust(string battleTag, string hash8, string author) =>
        SendSmurfHardware(HttpMethod.Delete, ItemPath(battleTag, hash8) + "/trust", new { author });

    public Task<MatchmakingProxyResult> IgnoreHardwareItem(string battleTag, string hash8, string reason, string author) =>
        SendSmurfHardware(HttpMethod.Post, ItemPath(battleTag, hash8) + "/ignore", new { reason, author });

    public Task<MatchmakingProxyResult> RevokeHardwareItemIgnore(string battleTag, string hash8, string author) =>
        SendSmurfHardware(HttpMethod.Delete, ItemPath(battleTag, hash8) + "/ignore", new { author });

    // BattleTags contain '#', so every segment is re-encoded for matchmaking.
    private static string Segment(string value) => Uri.EscapeDataString(value);

    private static string ItemPath(string battleTag, string hash8) =>
        $"/hardware/items/{Segment(battleTag)}/{Segment(hash8)}";

    private static string PairPath(string battleTagA, string battleTagB, string verdict) =>
        $"/links/{Segment(battleTagA)}/{Segment(battleTagB)}/{verdict}";

    private async Task<MatchmakingProxyResult> SendSmurfHardware(HttpMethod method, string path, object body = null)
    {
        using var request = CreateAdminRequest(method, MatchmakingApiUrl + SmurfDetectionPath + path);
        if (body != null) request.Content = CreateJsonContent(body);
        try
        {
            using var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                Log.Error("Matchmaking rejected the admin credentials for a smurf moderation {Method} request", method.Method);
                return MatchmakingProxyResult.Unavailable();
            }
            return new MatchmakingProxyResult((int)response.StatusCode, content);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // The message can carry the upstream address, so only the exception type is logged.
            Log.Error("Matchmaking smurf moderation request {Method} failed: {ExceptionType}", method.Method, ex.GetType().Name);
            return MatchmakingProxyResult.Unavailable();
        }
    }
}
