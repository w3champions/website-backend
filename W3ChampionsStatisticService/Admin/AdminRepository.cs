using MongoDB.Driver;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Ports;
using W3C.Domain.Tracing;
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

    public async Task<List<string>> SearchSmurfsFor(string tag)
    {
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("x-admin-secret", AdminSecret);
        var url = $"{MatchmakingApiUrl}/player/{HttpUtility.UrlEncode(tag)}/alts";
        var result = await httpClient.GetAsync(url);
        var content = await result.Content.ReadAsStringAsync();
        if (string.IsNullOrEmpty(content)) return null;
        var deserializeObject = JsonConvert.DeserializeObject<Aliases>(content);

        return deserializeObject.smurfs;
    }

    public async Task<List<GlobalChatBan>> GetChatBans()
    {
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("x-admin-secret", AdminSecret);
        var url = $"{MatchmakingApiUrl}/flo/globalChatBans";
        var result = await httpClient.GetAsync(url);
        var content = await result.Content.ReadAsStringAsync();
        if (string.IsNullOrEmpty(content)) return null;
        var deserializeObject = JsonConvert.DeserializeObject<PlayerChatBanWrapper>(content);

        var globalChatBans = new List<GlobalChatBan>();

        List<PlayerChatBan> banList = deserializeObject.playerBansList;

        if (!banList.Any())
        {
            return globalChatBans;
        }

        foreach (var item in deserializeObject.playerBansList)
        {
            var chatBanData = new GlobalChatBan();

            chatBanData.id = item.id;
            chatBanData.battleTag = item.player.name;

            if (item.banExpiresAt == null)
            {
                chatBanData.expiresAt = null;
            }
            else
            {
                chatBanData.expiresAt = DateTimeOffset.FromUnixTimeSeconds(item.banExpiresAt.seconds).DateTime;
            }

            globalChatBans.Add(chatBanData);
        }
        return globalChatBans;
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

    public class Aliases
    {
        public string battleTag { get; set; }
        public List<string> smurfs { get; set; }
    }
}
