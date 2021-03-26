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

        public async Task<ProxyUpdate> UpdateProxy(ProxyUpdate proxyUpdateData, string battleTag)
        {
            var proxiesForTag = await GetProxiesFor(battleTag);
            var newProxiesBeingAdded = new ProxyUpdate();

            if (proxiesForTag.nodeOverrides.Count != 0) // if the player has some proxies, check which are already set.
            { 
                foreach (var nodeOverride in proxyUpdateData.nodeOverrides) // run through all the proxies that were requested
                {
                    
                    foreach (var existingNodeOverride in proxiesForTag.nodeOverrides) // run through existing Node Overrides
                    {
                        if (nodeOverride != existingNodeOverride) // check if there is a match.
                        {
                            // add it to be requested
                            newProxiesBeingAdded.nodeOverrides.Add(nodeOverride);
                        }
                    }
                }
            } else 
            {
                foreach (var nodeOverride in proxyUpdateData.nodeOverrides)
                {
                    newProxiesBeingAdded.nodeOverrides.Add(nodeOverride);
                }
            }

            if (proxiesForTag.automaticNodeOverrides.Count > 0) 
            {
                foreach (var autoNodeOverride in proxyUpdateData.automaticNodeOverrides) // run through all the proxies that were requested
                {
                    foreach (var existingAutoNodeOverride in proxiesForTag.automaticNodeOverrides) // run through existing Auto Node Overrides
                    {
                        if (autoNodeOverride != existingAutoNodeOverride)
                        {
                            newProxiesBeingAdded.automaticNodeOverrides.Add(autoNodeOverride);
                        }
                        
                    }
                }
            } else 
            {
                foreach (var autoNodeOverride in proxyUpdateData.automaticNodeOverrides)
                {
                    newProxiesBeingAdded.nodeOverrides.Add(autoNodeOverride);
                }
            }

            // check if the proxy data is in the options
            // add it to a http request
            // send to matchmaking endpoint:
            // https://matchmaking-service.test.w3champions.com/{battleTag}/flo-proxies?secret={adminSecret}
            // TO DO

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
    }
}