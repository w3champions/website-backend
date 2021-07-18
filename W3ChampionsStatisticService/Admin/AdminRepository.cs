using System;
using System.Web;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Admin
{
    public class AdminRepository : IAdminRepository
    {
        private static readonly string MatchmakingApiUrl = Environment.GetEnvironmentVariable("MATCHMAKING_API") ?? "https://matchmaking-service.test.w3champions.com";
        private static readonly string MatchmakingAdminSecret = Environment.GetEnvironmentVariable("ADMIN_SECRET") ?? "300C018C-6321-4BAB-B289-9CB3DB760CBB";

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
            var result = await httpClient.GetAsync($"{MatchmakingApiUrl}/player/{HttpUtility.UrlEncode(battleTag)}/flo-proxies?secret={MatchmakingAdminSecret}");
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
            var serializedObject = JsonConvert.SerializeObject(newProxiesBeingAdded);
            var buffer = System.Text.Encoding.UTF8.GetBytes(serializedObject);
            var byteContent = new ByteArrayContent(buffer);
            byteContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            var result = await httpClient.PutAsync($"{MatchmakingApiUrl}/player/{HttpUtility.UrlEncode(battleTag)}/flo-proxies?secret={MatchmakingAdminSecret}", byteContent);

            return newProxiesBeingAdded;
        }

        private async Task<List<ProxiesData>> GetProxiesFromMatchmaking()
        {
            var httpClient = new HttpClient();
            var result = await httpClient.GetAsync($"{MatchmakingApiUrl}/flo/proxies?secret={MatchmakingAdminSecret}");
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
            var result = await httpClient.GetAsync($"{MatchmakingApiUrl}/player/{HttpUtility.UrlEncode(tag)}/alts?secret={MatchmakingAdminSecret}");
            var content = await result.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(content)) return null;
            var deserializeObject = JsonConvert.DeserializeObject<Aliases>(content);
            
            return deserializeObject.smurfs;
        }

        public class Aliases
        {
            public string battleTag {get; set; }
            public List<string> smurfs {get; set; }
        }
    }
}