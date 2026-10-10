using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace W3C.Domain.MatchmakingService;

/// <summary>
/// Proxy for the matchmaking commercial-events admin API (/admin/commercial-events). Unlike the other client methods,
/// failures throw MatchmakingPassthroughException instead of going through HandleMMError, so the website receives
/// matchmaking's error body (code, field, rule, data) unchanged.
/// </summary>
public partial class MatchmakingServiceClient
{
    // camelCase without null fields (absent and null both mean "not given"); dates in the exact
    // Date.prototype.toISOString() format that matchmaking produces.
    private static readonly JsonSerializerSettings CommercialEventsBodySettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
        DateTimeZoneHandling = DateTimeZoneHandling.Utc,
        DateFormatString = "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fff'Z'",
    };

    // Date-like strings inside free-form values (audit details) stay strings; typed DateTime properties still parse.
    private static readonly JsonSerializerSettings CommercialEventsResponseSettings = new()
    {
        DateParseHandling = DateParseHandling.None,
    };

    public async Task<List<CommercialEventAllocationDto>> GetCommercialEventAllocations() =>
        await SendCommercialEvents<List<CommercialEventAllocationDto>>(HttpMethod.Get, "/allocations") ?? [];

    public Task<CommercialEventAllocationDto> CreateCommercialEventAllocation(CommercialEventAllocationRequest request, string actingBattleTag) =>
        SendCommercialEvents<CommercialEventAllocationDto>(HttpMethod.Post, "/allocations", WithActingBattleTag(request, actingBattleTag));

    public Task<CommercialEventAllocationDto> UpdateCommercialEventAllocation(string allocationId, CommercialEventAllocationRequest request, string actingBattleTag) =>
        SendCommercialEvents<CommercialEventAllocationDto>(HttpMethod.Put, $"/allocations/{PathSegment(allocationId)}", WithActingBattleTag(request, actingBattleTag));

    public Task<CommercialEventAllocationDto> AddCommercialEventAllocationMember(string allocationId, string battleTag, string actingBattleTag) =>
        SendCommercialEvents<CommercialEventAllocationDto>(HttpMethod.Put, $"/allocations/{PathSegment(allocationId)}/members/{PathSegment(battleTag)}", WithActingBattleTag(null, actingBattleTag));

    public Task<CommercialEventAllocationDto> RemoveCommercialEventAllocationMember(string allocationId, string battleTag, string actingBattleTag) =>
        SendCommercialEvents<CommercialEventAllocationDto>(HttpMethod.Delete, $"/allocations/{PathSegment(allocationId)}/members/{PathSegment(battleTag)}", WithActingBattleTag(null, actingBattleTag));

    public Task<CommercialEventAllocationDto> EndCommercialEventAllocation(string allocationId, string actingBattleTag) =>
        SendCommercialEvents<CommercialEventAllocationDto>(HttpMethod.Post, $"/allocations/{PathSegment(allocationId)}/end", WithActingBattleTag(null, actingBattleTag));

    public Task DeleteCommercialEventAllocation(string allocationId, string actingBattleTag) =>
        SendCommercialEventsRaw(HttpMethod.Delete, $"/allocations/{PathSegment(allocationId)}", WithActingBattleTag(null, actingBattleTag));

    public async Task<List<CommercialEventPeriodUsageDto>> GetCommercialEventAllocationPeriods(string allocationId) =>
        await SendCommercialEvents<List<CommercialEventPeriodUsageDto>>(HttpMethod.Get, $"/allocations/{PathSegment(allocationId)}/periods") ?? [];

    // Kestrel has already decoded route values; re-encode them so '#' and friends survive as one path segment.
    private static string PathSegment(string value) => Uri.EscapeDataString(value);

    // actingBattleTag is set last, so it always comes from the bearer token.
    private static string WithActingBattleTag(object body, string actingBattleTag)
    {
        var json = body == null ? new JObject() : JObject.FromObject(body, JsonSerializer.Create(CommercialEventsBodySettings));
        json["actingBattleTag"] = actingBattleTag;
        return JsonConvert.SerializeObject(json, CommercialEventsBodySettings);
    }

    private async Task<T> SendCommercialEvents<T>(HttpMethod method, string path, string jsonBody = null)
        where T : class
    {
        var content = await SendCommercialEventsRaw(method, path, jsonBody);
        return string.IsNullOrEmpty(content) ? null : JsonConvert.DeserializeObject<T>(content, CommercialEventsResponseSettings);
    }

    private async Task<string> SendCommercialEventsRaw(HttpMethod method, string path, string jsonBody = null)
    {
        using var request = new HttpRequestMessage(method, $"{MatchmakingApiUrl}/admin/commercial-events{path}");
        AdminSecretProvider.AddTo(request.Headers);
        if (jsonBody != null)
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request);
        }
        catch (HttpRequestException ex)
        {
            throw new MatchmakingPassthroughException(HttpStatusCode.BadGateway, null, ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new MatchmakingPassthroughException(HttpStatusCode.GatewayTimeout, null, ex);
        }

        using (response)
        {
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new MatchmakingPassthroughException(response.StatusCode, content);
            }
            return content;
        }
    }
}
