using System;
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
        private static readonly string MatchmakingAdminSecret = "SECRET"; // Environment.GetEnvironmentVariable("ADMIN_SECRET") ?? "300C018C-6321-4BAB-B289-9CB3DB760CBB";

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

        public async Task<List<ProxiesResponse>> GetProxiesFor(string battleTag)
        {
            var httpClient = new HttpClient();
            var result = await httpClient.GetAsync($"{MatchmakingApiUrl}/player/{battleTag}/flo-proxies?secret={MatchmakingAdminSecret}");
            var content = await result.Content.ReadAsStringAsync();
            
            if (result.StatusCode == HttpStatusCode.NotFound) return new List<ProxiesResponse>();
            //if (string.IsNullOrEmpty(content)) return null;
            
            var deserializeObject = JsonConvert.DeserializeObject<List<ProxiesData>>(content);
            var allPlayerProxies = new List<ProxiesResponse>();

            foreach (var proxy in deserializeObject)
            {
                allPlayerProxies.Add(RemoveProxiesPortAndAddress(proxy));
            }

            return allPlayerProxies;
        }

        private ProxiesResponse RemoveProxiesPortAndAddress(ProxiesData proxiesFromMatchmaking)
        {
            var proxies = new ProxiesResponse();
            proxies.id = proxiesFromMatchmaking.id;
            proxies.nodeId = proxiesFromMatchmaking.nodeId;
            return proxies;
        }

        public async Task<List<ProxiesResponse>> UpdateProxy(List<ProxyUpdate> proxyUpdateData, string battleTag)
        {
            var allProxies = await GetProxiesFromMatchmaking();

            // check if the proxy data is in the options
            // add it to a http request
            // send to matchmaking endpoint:
            // https://matchmaking-service.test.w3champions.com/{battleTag}/flo-proxies?secret={adminSecret}
            // TO DO

            return new List<ProxiesResponse>();
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
    }
}