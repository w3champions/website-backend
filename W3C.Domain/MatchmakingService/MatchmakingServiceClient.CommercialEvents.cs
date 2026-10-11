using System;
using System.Collections.Generic;
using System.Linq;
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
        await SendCommercialEventsList<CommercialEventAllocationDto>(HttpMethod.Get, "/allocations");

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
        await SendCommercialEventsList<CommercialEventPeriodUsageDto>(HttpMethod.Get, $"/allocations/{PathSegment(allocationId)}/periods");

    public async Task<List<CommercialEventDto>> GetCommercialEvents(string status, string phase, string allocationId, string q) =>
        await SendCommercialEventsList<CommercialEventDto>(HttpMethod.Get, "/events" + Query(("status", status), ("phase", phase), ("allocationId", allocationId), ("q", q)));

    public Task<CommercialEventDetailDto> GetCommercialEvent(string eventId) =>
        SendCommercialEvents<CommercialEventDetailDto>(HttpMethod.Get, $"/events/{PathSegment(eventId)}");

    public Task<CommercialEventDetailDto> CreateCommercialEvent(CommercialEventCreateRequest request, string actingBattleTag) =>
        SendCommercialEvents<CommercialEventDetailDto>(HttpMethod.Post, "/events", WithActingBattleTag(request, actingBattleTag));

    public Task<CommercialEventDetailDto> UpdateCommercialEvent(string eventId, CommercialEventUpdateRequest request, string actingBattleTag) =>
        SendCommercialEvents<CommercialEventDetailDto>(HttpMethod.Put, $"/events/{PathSegment(eventId)}", WithActingBattleTag(request, actingBattleTag));

    public Task<CommercialEventDetailDto> MoveCommercialEvent(string eventId, CommercialEventMoveRequest request, string actingBattleTag) =>
        SendCommercialEvents<CommercialEventDetailDto>(HttpMethod.Post, $"/events/{PathSegment(eventId)}/move", WithActingBattleTag(request, actingBattleTag));

    public Task<CommercialEventDetailDto> CloseCommercialEvent(string eventId, string actingBattleTag) =>
        SendCommercialEvents<CommercialEventDetailDto>(HttpMethod.Post, $"/events/{PathSegment(eventId)}/close", WithActingBattleTag(null, actingBattleTag));

    public Task<CommercialEventDetailDto> SuspendCommercialEvent(string eventId, CommercialEventSuspendRequest request, string actingBattleTag) =>
        SendCommercialEvents<CommercialEventDetailDto>(HttpMethod.Post, $"/events/{PathSegment(eventId)}/suspend", WithActingBattleTag(request, actingBattleTag));

    public Task<CommercialEventDetailDto> UnsuspendCommercialEvent(string eventId, string actingBattleTag) =>
        SendCommercialEvents<CommercialEventDetailDto>(HttpMethod.Post, $"/events/{PathSegment(eventId)}/unsuspend", WithActingBattleTag(null, actingBattleTag));

    public Task<CommercialEventPeopleDto> GetCommercialEventPeople(string eventId) =>
        SendCommercialEvents<CommercialEventPeopleDto>(HttpMethod.Get, $"/events/{PathSegment(eventId)}/people");

    public Task<CommercialEventPeopleDto> AddCommercialEventPerson(string eventId, string battleTag, CommercialEventPersonRequest request, string actingBattleTag) =>
        SendCommercialEvents<CommercialEventPeopleDto>(HttpMethod.Put, $"/events/{PathSegment(eventId)}/people/{PathSegment(battleTag)}", WithActingBattleTag(request, actingBattleTag));

    public Task<CommercialEventPeopleDto> RemoveCommercialEventPerson(string eventId, string battleTag, string actingBattleTag) =>
        SendCommercialEvents<CommercialEventPeopleDto>(HttpMethod.Delete, $"/events/{PathSegment(eventId)}/people/{PathSegment(battleTag)}", WithActingBattleTag(null, actingBattleTag));

    public Task<CommercialEventGamesPageDto> GetCommercialEventGames(string eventId, string cursor, string limit) =>
        SendCommercialEvents<CommercialEventGamesPageDto>(HttpMethod.Get, $"/events/{PathSegment(eventId)}/games" + Query(("cursor", cursor), ("limit", limit)));

    public async Task<List<CommercialEventActiveGameDto>> GetActiveCommercialEventGames() =>
        await SendCommercialEventsList<CommercialEventActiveGameDto>(HttpMethod.Get, "/games/active");

    public Task TerminateCommercialEventGame(string matchId, string actingBattleTag) =>
        SendCommercialEventsRaw(HttpMethod.Post, $"/games/{PathSegment(matchId)}/terminate", WithActingBattleTag(null, actingBattleTag));

    public async Task<List<CommercialEventAuditEntryDto>> GetCommercialEventAudit(string eventId, string allocationId) =>
        await SendCommercialEventsList<CommercialEventAuditEntryDto>(HttpMethod.Get, "/audit" + Query(("eventId", eventId), ("allocationId", allocationId)));

    // A read, so no acting admin: the body is serialized directly instead of through WithActingBattleTag.
    // A null list is omitted by NullValueHandling.Ignore, and matchmaking answers 400 INVALID_REQUEST {field: 'battleTags'}.
    public async Task<List<CommercialEventRoleHintsDto>> GetCommercialEventRoleHints(IEnumerable<string> battleTags) =>
        await SendCommercialEventsList<CommercialEventRoleHintsDto>(
            HttpMethod.Post,
            "/roles/lookup",
            JsonConvert.SerializeObject(new CommercialEventRoleLookupRequest { BattleTags = battleTags?.ToList() }, CommercialEventsBodySettings));

    // Forwards only non-empty values, each escaped; matchmaking validates them.
    private static string Query(params (string Name, string Value)[] parameters)
    {
        var pairs = parameters
            .Where(parameter => !string.IsNullOrEmpty(parameter.Value))
            .Select(parameter => $"{parameter.Name}={Uri.EscapeDataString(parameter.Value)}")
            .ToList();
        return pairs.Count == 0 ? "" : "?" + string.Join("&", pairs);
    }

    // Kestrel has already decoded route values; re-encode them so '#' and friends survive as one path segment.
    private static string PathSegment(string value) => Uri.EscapeDataString(value);

    // actingBattleTag is set last, so it always comes from the bearer token.
    private static string WithActingBattleTag(object body, string actingBattleTag)
    {
        var json = body == null ? new JObject() : JObject.FromObject(body, JsonSerializer.Create(CommercialEventsBodySettings));
        json["actingBattleTag"] = actingBattleTag;
        return JsonConvert.SerializeObject(json, CommercialEventsBodySettings);
    }

    // Object endpoints: an empty or JSON null success body is contract drift, so it is a 502 rather than a null result.
    private async Task<T> SendCommercialEvents<T>(HttpMethod method, string path, string jsonBody = null)
        where T : class =>
        await SendCommercialEventsLenient<T>(method, path, jsonBody)
        ?? throw new MatchmakingPassthroughException(HttpStatusCode.BadGateway, null);

    // Collection endpoints: an empty or JSON null success body means an empty list.
    private async Task<List<TItem>> SendCommercialEventsList<TItem>(HttpMethod method, string path, string jsonBody = null) =>
        await SendCommercialEventsLenient<List<TItem>>(method, path, jsonBody) ?? [];

    private async Task<T> SendCommercialEventsLenient<T>(HttpMethod method, string path, string jsonBody)
        where T : class
    {
        var content = await SendCommercialEventsRaw(method, path, jsonBody);
        if (string.IsNullOrEmpty(content)) return null;
        try
        {
            return JsonConvert.DeserializeObject<T>(content, CommercialEventsResponseSettings);
        }
        catch (JsonException ex)
        {
            // A success body that does not match the DTO (contract drift) is an upstream fault, not a website bug.
            throw new MatchmakingPassthroughException(HttpStatusCode.BadGateway, null, ex);
        }
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
        // HttpClient documents OperationCanceledException for timeouts; TaskCanceledException derives from it.
        catch (OperationCanceledException ex)
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
