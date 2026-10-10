using W3C.Domain;
using MongoDB.Driver;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Ports;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Extensions;
namespace W3ChampionsStatisticService.Admin;

[Trace]
public class AdminRepository(MongoClient mongoClient, IHttpClientFactory httpClientFactory) : MongoDbRepositoryBase(mongoClient), IAdminRepository
{
    private static readonly string MatchmakingApiUrl = Environment.GetEnvironmentVariable("MATCHMAKING_API") ?? "https://matchmaking-service.test.w3champions.com";

    // The admin secret goes on each request (CreateAdminRequest), never on DefaultRequestHeaders:
    // factory clients share state across calls.
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient(HttpClientNames.Matchmaking);

    private async Task<HttpResponseMessage> SendAdminAsync(HttpMethod method, string url, HttpContent content = null)
    {
        using var request = CreateAdminRequest(method, url);
        request.Content = content;
        return await _httpClient.SendAsync(request);
    }

    public async Task<List<ProxiesResponse>> GetProxies()
    {
        var allProxies = await GetProxiesFromMatchmaking();
        var allProxiesWithoutAddressOrPort = new List<ProxiesResponse>();

        foreach (var proxy in allProxies)
        {
            allProxiesWithoutAddressOrPort.Add(RemoveProxiesPortAndAddress(proxy));
        }

        return allProxiesWithoutAddressOrPort;
    }

    public async Task<FloProxies> GetProxiesFor(string battleTag)
    {
        var url = $"{MatchmakingApiUrl}/player/{HttpUtility.UrlEncode(battleTag)}/flo-proxies";
        var result = await SendAdminAsync(HttpMethod.Get, url);
        var content = await result.Content.ReadAsStringAsync();

        if (result.StatusCode == HttpStatusCode.NotFound) return new FloProxies();
        //if (string.IsNullOrEmpty(content)) return null;

        var deserializeObject = JsonConvert.DeserializeObject<FloProxies>(content);

        return deserializeObject;
    }

    private ProxiesResponse RemoveProxiesPortAndAddress(ProxiesData proxiesFromMatchmaking)
    {
        var proxies = new ProxiesResponse();
        proxies.id = proxiesFromMatchmaking.id;
        proxies.nodeId = proxiesFromMatchmaking.nodeId;
        return proxies;
    }

    public async Task<ProxyUpdate> UpdateProxies(ProxyUpdate proxyUpdateData, string battleTag)
    {
        var newProxiesBeingAdded = new ProxyUpdate();

        foreach (var nodeOverride in proxyUpdateData.nodeOverrides)
        {
            newProxiesBeingAdded.nodeOverrides.Add(nodeOverride);
        }

        foreach (var autoNodeOverride in proxyUpdateData.automaticNodeOverrides)
        {
            newProxiesBeingAdded.automaticNodeOverrides.Add(autoNodeOverride);
        }

        // send request to mm with all the node values
        var url = $"{MatchmakingApiUrl}/player/{HttpUtility.UrlEncode(battleTag)}/flo-proxies";
        var serializedObject = JsonConvert.SerializeObject(newProxiesBeingAdded);
        var buffer = System.Text.Encoding.UTF8.GetBytes(serializedObject);
        var byteContent = new ByteArrayContent(buffer);
        byteContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        var result = await SendAdminAsync(HttpMethod.Put, url, byteContent);

        return newProxiesBeingAdded;
    }

    private async Task<List<ProxiesData>> GetProxiesFromMatchmaking()
    {
        var url = $"{MatchmakingApiUrl}/flo/proxies";
        var result = await SendAdminAsync(HttpMethod.Get, url);
        var content = await result.Content.ReadAsStringAsync();
        if (string.IsNullOrEmpty(content)) return null;
        var deserializeObject = JsonConvert.DeserializeObject<List<ProxiesData>>(content);
        return deserializeObject;
    }

    public class ProxiesData
    {
        public string id { get; set; }
        public int nodeId { get; set; }
        public int port { get; set; }
        public string address { get; set; }
    }

    [Obsolete("Use QuerySmurfsFor instead")]
    public async Task<List<string>> SearchSmurfsFor(string tag)
    {
        var result = await QuerySmurfsFor("battleTag", tag, false, 1);
        return [.. result.connectedBattleTags];
    }

    public async Task<GlobalChatBanResponse> GetChatBans(string query, string nextId)
    {
        string url = $"{MatchmakingApiUrl}/flo/globalChatBans";
        if (query != null)
        {
            url += $"?query={HttpUtility.UrlEncode(query)}";
        }
        else if (nextId != null)
        {
            url += $"?nextId={nextId}";
        }
        HttpResponseMessage result = await SendAdminAsync(HttpMethod.Get, url);
        string content = await result.Content.ReadAsStringAsync();
        if (string.IsNullOrEmpty(content)) return null;
        var deserializeObject = JsonConvert.DeserializeObject<PlayerChatBanWrapper>(content);

        var globalChatBans = new List<GlobalChatBan>();

        if (deserializeObject.playerBansList.Count == 0)
        {
            return new GlobalChatBanResponse
            {
                globalChatBans = [],
                next_id = 0,
            };
        }

        foreach (PlayerChatBan item in deserializeObject.playerBansList)
        {
            var globalChatBan = new GlobalChatBan
            {
                id = item.id,
                battleTag = item.player.name,
                createdAt = DateTimeOffset.FromUnixTimeSeconds(item.createdAt.seconds).DateTime,
                expiresAt = item.banExpiresAt == null ? null : DateTimeOffset.FromUnixTimeSeconds(item.banExpiresAt.seconds).DateTime,
                author = item.author?.value,
            };

            globalChatBans.Add(globalChatBan);
        }
        return new GlobalChatBanResponse
        {
            globalChatBans = globalChatBans,
            next_id = deserializeObject.next_id?.value,
        };
    }

    public async Task<HttpStatusCode> PutChatBan(ChatBanPutDto chatBan)
    {
        var url = $"{MatchmakingApiUrl}/flo/globalChatBans";
        var serializedObject = JsonConvert.SerializeObject(chatBan);
        var buffer = System.Text.Encoding.UTF8.GetBytes(serializedObject);
        var byteContent = new ByteArrayContent(buffer);
        byteContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        var result = await SendAdminAsync(HttpMethod.Post, url, byteContent);
        return result.StatusCode;
    }

    public async Task<HttpStatusCode> DeleteChatBan(string id)
    {
        var url = $"{MatchmakingApiUrl}/flo/globalChatBans/{id}";
        var result = await SendAdminAsync(HttpMethod.Delete, url);
        return result.StatusCode;
    }

    public async Task<List<SmurfDetection.IgnoredIdentifier>> GetIgnoredIdentifiers(string type, string continuationToken)
    {
        var url = $"{MatchmakingApiUrl}/admin/smurf-detection/ignored-identifiers";
        if (continuationToken != null)
        {
            url += $"?continuationToken={HttpUtility.UrlEncode(continuationToken)}";
        }
        else if (type != null)
        {
            url += $"?type={HttpUtility.UrlEncode(type)}";
        }
        else
        {
            throw new ArgumentException("Either type or continuationToken must be provided");
        }
        var result = await SendAdminAsync(HttpMethod.Get, url);
        await result.ThrowIfError();
        string content = await result.Content.ReadAsStringAsync();
        if (string.IsNullOrEmpty(content)) return [];
        var deserializeObject = JsonConvert.DeserializeObject<GetIgnoredIdentifiersResponse>(content);
        return deserializeObject.identifiers;
    }

    public async Task<SmurfDetection.IgnoredIdentifier> AddIgnoredIdentifier(string type, string identifier, string reason, string author)
    {
        using var request = BuildAddIgnoredIdentifierRequest(type, identifier, reason, author);
        var result = await _httpClient.SendAsync(request);
        await result.ThrowIfError();
        string content = await result.Content.ReadAsStringAsync();
        return ParseAddIgnoredIdentifierResponse(content);
    }

    public async Task<HttpStatusCode> DeleteIgnoredIdentifier(string id)
    {
        using var request = BuildDeleteIgnoredIdentifierRequest(id);
        var result = await _httpClient.SendAsync(request);
        await result.ThrowIfError();
        return result.StatusCode;
    }

    public async Task<List<string>> GetPossibleIdentifierTypes()
    {
        var url = $"{MatchmakingApiUrl}/admin/smurf-detection/possible-identifier-types";
        var result = await SendAdminAsync(HttpMethod.Get, url);
        await result.ThrowIfError();
        string content = await result.Content.ReadAsStringAsync();
        if (string.IsNullOrEmpty(content)) return [];
        var deserializeObject = JsonConvert.DeserializeObject<PossibleIdentifierTypesResponse>(content);
        return deserializeObject.possibleIdentifierTypes;
    }

    public async Task<SmurfDetection.SmurfDetectionResult> QuerySmurfsFor(string identifierType, string identifier, bool includeExplanation = false, int iterationDepth = 1)
    {
        var generateExplanation = includeExplanation ? "true" : "false";
        var url = $"{MatchmakingApiUrl}/admin/smurf-detection/query-smurfs?identifierType={identifierType}&identifier={HttpUtility.UrlEncode(identifier)}&generateExplanation={generateExplanation}&iterationDepth={iterationDepth}";
        var result = await SendAdminAsync(HttpMethod.Get, url);
        await result.ThrowIfError();
        var content = await result.Content.ReadAsStringAsync();
        if (string.IsNullOrEmpty(content)) return null;
        var deserializeObject = JsonConvert.DeserializeObject<SmurfDetection.SmurfDetectionResult>(content);

        return deserializeObject;
    }

    // Request builders and the response parsers are internal static so the matchmaking route and
    // body contract (matchmaking-service smurf-detection/api/admin.ts) is unit-testable.
    internal static HttpRequestMessage BuildAddIgnoredIdentifierRequest(string type, string identifier, string reason, string author)
    {
        var request = CreateAdminRequest(HttpMethod.Post, $"{MatchmakingApiUrl}/admin/smurf-detection/ignored-identifier");
        request.Content = CreateJsonContent(new { type, value = identifier, reason, author });
        return request;
    }

    internal static HttpRequestMessage BuildDeleteIgnoredIdentifierRequest(string id)
    {
        var request = CreateAdminRequest(HttpMethod.Delete, $"{MatchmakingApiUrl}/admin/smurf-detection/ignored-identifier");
        request.Content = CreateJsonContent(new { id });
        return request;
    }

    internal static SmurfDetection.IgnoredIdentifier ParseAddIgnoredIdentifierResponse(string content)
    {
        if (string.IsNullOrEmpty(content)) return null;
        return JsonConvert.DeserializeObject<AddIgnoredIdentifierResponse>(content)?.newIdentifier;
    }

    private static HttpRequestMessage CreateAdminRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        AdminSecretProvider.AddTo(request.Headers);
        return request;
    }

    private static StringContent CreateJsonContent(object body) =>
        new(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
}
