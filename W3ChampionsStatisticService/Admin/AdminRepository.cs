using MongoDB.Driver;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Ports;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Extensions;
namespace W3ChampionsStatisticService.Admin;

[Trace]
public class AdminRepository(MongoClient mongoClient) : MongoDbRepositoryBase(mongoClient), IAdminRepository
{
    private static readonly string MatchmakingApiUrl = Environment.GetEnvironmentVariable("MATCHMAKING_API") ?? "https://matchmaking-service.test.w3champions.com";
    private static readonly string AdminSecret = Environment.GetEnvironmentVariable("ADMIN_SECRET") ?? "300C018C-6321-4BAB-B289-9CB3DB760CBB";

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
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("x-admin-secret", AdminSecret);
        var url = $"{MatchmakingApiUrl}/player/{HttpUtility.UrlEncode(battleTag)}/flo-proxies";
        var result = await httpClient.GetAsync(url);
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
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("x-admin-secret", AdminSecret);
        var url = $"{MatchmakingApiUrl}/player/{HttpUtility.UrlEncode(battleTag)}/flo-proxies";
        var serializedObject = JsonConvert.SerializeObject(newProxiesBeingAdded);
        var buffer = System.Text.Encoding.UTF8.GetBytes(serializedObject);
        var byteContent = new ByteArrayContent(buffer);
        byteContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        var result = await httpClient.PutAsync(url, byteContent);

        return newProxiesBeingAdded;
    }

    private async Task<List<ProxiesData>> GetProxiesFromMatchmaking()
    {
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("x-admin-secret", AdminSecret);
        var url = $"{MatchmakingApiUrl}/flo/proxies";
        var result = await httpClient.GetAsync(url);
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
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("x-admin-secret", AdminSecret);
        string url = $"{MatchmakingApiUrl}/flo/globalChatBans";
        if (query != null)
        {
            url += $"?query={HttpUtility.UrlEncode(query)}";
        }
        else if (nextId != null)
        {
            url += $"?nextId={nextId}";
        }
        HttpResponseMessage result = await httpClient.GetAsync(url);
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
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("x-admin-secret", AdminSecret);
        var url = $"{MatchmakingApiUrl}/flo/globalChatBans";
        var serializedObject = JsonConvert.SerializeObject(chatBan);
        var buffer = System.Text.Encoding.UTF8.GetBytes(serializedObject);
        var byteContent = new ByteArrayContent(buffer);
        byteContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        var result = await httpClient.PostAsync(url, byteContent);
        return result.StatusCode;
    }

    public async Task<HttpStatusCode> DeleteChatBan(string id)
    {
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("x-admin-secret", AdminSecret);
        var url = $"{MatchmakingApiUrl}/flo/globalChatBans/{id}";
        var result = await httpClient.DeleteAsync(url);
        return result.StatusCode;
    }

    public async Task<SmurfDetection.IgnoredIdentifier> GetIgnoredIdentifier(string type, string identifier)
    {
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("x-admin-secret", AdminSecret);
        var url = $"{MatchmakingApiUrl}/admin/smurf-detection/ignored-identifiers/{type}/{identifier}";
        var result = await httpClient.GetAsync(url);
        await result.ThrowIfError();
        string content = await result.Content.ReadAsStringAsync();
        if (string.IsNullOrEmpty(content)) return null;
        var deserializeObject = JsonConvert.DeserializeObject<GetIgnoredIdentifierResponse>(content);
        return deserializeObject.ignoredIdentifier;
    }

    public async Task<List<SmurfDetection.IgnoredIdentifier>> GetIgnoredIdentifiers(string type, string continuationToken)
    {
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("x-admin-secret", AdminSecret);
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
        var result = await httpClient.GetAsync(url);
        await result.ThrowIfError();
        string content = await result.Content.ReadAsStringAsync();
        if (string.IsNullOrEmpty(content)) return [];
        var deserializeObject = JsonConvert.DeserializeObject<GetIgnoredIdentifiersResponse>(content);
        return deserializeObject.identifiers;
    }

    public async Task<SmurfDetection.IgnoredIdentifier> AddIgnoredIdentifier(string type, string identifier, string reason, string author)
    {
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("x-admin-secret", AdminSecret);
        var url = $"{MatchmakingApiUrl}/admin/smurf-detection/ignored-identifiers";
        var serializedObject = JsonConvert.SerializeObject(new { type, identifier, reason, author });
        var buffer = System.Text.Encoding.UTF8.GetBytes(serializedObject);
        var byteContent = new ByteArrayContent(buffer);
        byteContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        var result = await httpClient.PostAsync(url, byteContent);
        await result.ThrowIfError();
        string content = await result.Content.ReadAsStringAsync();
        if (string.IsNullOrEmpty(content)) return null;
        var savedIgnoredIdentifier = JsonConvert.DeserializeObject<SmurfDetection.IgnoredIdentifier>(content);
        return savedIgnoredIdentifier;
    }

    public async Task<HttpStatusCode> DeleteIgnoredIdentifier(string id)
    {
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("x-admin-secret", AdminSecret);
        var url = $"{MatchmakingApiUrl}/admin/smurf-detection/ignored-identifiers/{id}";
        var result = await httpClient.DeleteAsync(url);
        await result.ThrowIfError();
        return result.StatusCode;
    }

    public async Task<List<string>> GetPossibleIdentifierTypes()
    {
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("x-admin-secret", AdminSecret);
        var url = $"{MatchmakingApiUrl}/admin/smurf-detection/possible-identifier-types";
        var result = await httpClient.GetAsync(url);
        await result.ThrowIfError();
        string content = await result.Content.ReadAsStringAsync();
        if (string.IsNullOrEmpty(content)) return [];
        var deserializeObject = JsonConvert.DeserializeObject<PossibleIdentifierTypesResponse>(content);
        return deserializeObject.possibleIdentifierTypes;
    }

    public async Task<SmurfDetection.SmurfDetectionResult> QuerySmurfsFor(string identifierType, string identifier, bool includeExplanation = false, int iterationDepth = 1)
    {
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("x-admin-secret", AdminSecret);
        var generateExplanation = includeExplanation ? "true" : "false";
        var url = $"{MatchmakingApiUrl}/admin/smurf-detection/query-smurfs?identifierType={identifierType}&identifier={HttpUtility.UrlEncode(identifier)}&generateExplanation={generateExplanation}&iterationDepth={iterationDepth}";
        var result = await httpClient.GetAsync(url);
        await result.ThrowIfError();
        var content = await result.Content.ReadAsStringAsync();
        if (string.IsNullOrEmpty(content)) return null;
        var deserializeObject = JsonConvert.DeserializeObject<SmurfDetection.SmurfDetectionResult>(content);

        return deserializeObject;
    }
}
