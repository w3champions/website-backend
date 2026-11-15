using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Admin;

/// <summary>Filter values of the ban list exactly as the browser sent them; null means absent.</summary>
public sealed record BanListFilter(string Status, string Author, string Account, string MachineOnly,
    string Sort, string Dir, string Page, string PageSize);

/// <summary>Forwarding of the matchmaking ban admin API; WB adds the caller's identity and machine-ban permission.</summary>
public partial class AdminRepository
{
    private const string MachineBanPermissionCode = "MACHINE_BAN_PERMISSION";

    public Task<MatchmakingProxyResult> ListBans(BanListFilter filter, bool machineAccess) =>
        SendBans(HttpMethod.Get, BansUrl("", machineAccess,
            ("status", filter.Status), ("author", filter.Author), ("account", filter.Account), ("machineOnly", filter.MachineOnly),
            ("sort", filter.Sort), ("dir", filter.Dir), ("page", filter.Page), ("pageSize", filter.PageSize)));

    public Task<MatchmakingProxyResult> GetBanAuthors(bool machineAccess) =>
        SendBans(HttpMethod.Get, BansUrl("/authors", machineAccess));

    public Task<MatchmakingProxyResult> LookupBanAccounts(string q, bool machineAccess) =>
        SendBans(HttpMethod.Get, BansUrl("/account-lookup", machineAccess, ("q", q)));

    public Task<MatchmakingProxyResult> GetBanAccount(string accountKey, bool machineAccess) =>
        SendBans(HttpMethod.Get, BansUrl($"/accounts/{Segment(accountKey)}", machineAccess));

    public Task<MatchmakingProxyResult> CreateBan([NoTrace] JsonObject body, string actor, bool machineAccess) =>
        SendBans(HttpMethod.Post, BansUrl("", machineAccess), ForwardedBody(body, actor));

    public Task<MatchmakingProxyResult> EditBan(string id, [NoTrace] JsonObject body, string actor, bool machineAccess) =>
        SendBans(HttpMethod.Patch, BansUrl($"/{Segment(id)}", machineAccess), ForwardedBody(body, actor));

    public Task<MatchmakingProxyResult> RevokeBan(string id, [NoTrace] JsonObject body, string actor, bool machineAccess) =>
        SendBans(HttpMethod.Post, BansUrl($"/{Segment(id)}/revoke", machineAccess), ForwardedBody(body, actor));

    // The three machine routes: BansController calls them only after its AdminModeration check, so machineAccess is
    // true in practice; it is still the caller's flag, never a constant, so matchmaking makes the final decision.
    public Task<MatchmakingProxyResult> GetBanAccountMachineIds(string accountKey, bool machineAccess) =>
        SendBans(HttpMethod.Get, BansUrl($"/accounts/{Segment(accountKey)}/machine-ids", machineAccess));

    public Task<MatchmakingProxyResult> GetBanMachineIdAccounts([NoTrace] string machineId, string excludeAccount, bool machineAccess) =>
        SendBans(HttpMethod.Get, BansUrl($"/machine-ids/{Segment(machineId)}/accounts", machineAccess, ("excludeAccount", excludeAccount)));

    public Task<MatchmakingProxyResult> GetBanMachineImpact([NoTrace] JsonObject body, bool machineAccess) =>
        SendBans(HttpMethod.Post, BansUrl("/machine-impact", machineAccess), ForwardedBody(body, null));

    public Task<MatchmakingProxyResult> GetBanStatuses([NoTrace] JsonObject body, bool machineAccess) =>
        SendBans(HttpMethod.Post, BansUrl("/status", machineAccess), ForwardedBody(body, null));

    private Task<MatchmakingProxyResult> SendBans(HttpMethod method, string url, string json = null) =>
        SendProxy(method, url, json == null ? null : new StringContent(json, Encoding.UTF8, "application/json"),
            IsMachineBanPermissionRefusal);

    internal static string BansUrl(string path, bool machineAccess, params (string Key, string Value)[] query)
    {
        var pairs = query
            .Where(q => !string.IsNullOrEmpty(q.Value))
            .Select(q => $"{q.Key}={Uri.EscapeDataString(q.Value)}")
            .ToList();
        if (machineAccess) pairs.Add("machineAccess=true");
        return MatchmakingApiUrl + "/admin/bans" + path + (pairs.Count == 0 ? "" : "?" + string.Join("&", pairs));
    }

    internal static string ForwardedBody(JsonObject body, string actor)
    {
        var copy = body.DeepClone().AsObject();
        copy.Remove("actor");
        copy.Remove("machineAccess");
        if (actor != null) copy["actor"] = actor;
        return copy.ToJsonString();
    }

    internal static bool IsMachineBanPermissionRefusal(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("errors", out var errors)
                && errors.ValueKind == JsonValueKind.Array
                && errors.EnumerateArray().Any(e => e.ValueKind == JsonValueKind.Object
                    && e.TryGetProperty("code", out var code)
                    && code.ValueKind == JsonValueKind.String
                    && code.GetString() == MachineBanPermissionCode);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return false;
        }
    }
}
