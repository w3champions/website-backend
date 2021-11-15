using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Admin;
using W3ChampionsStatisticService.Ports;

namespace WC3ChampionsStatisticService.UnitTests.TestRepositories
{
    public class AdminTestRepository : IAdminRepository
    {
        private static readonly string MatchmakingApiUrl = Environment.GetEnvironmentVariable("https://matchmaking-service.test.w3champions.com");
        private static readonly string MatchmakingAdminSecret = Environment.GetEnvironmentVariable("adminSecret");

        public Task<HttpStatusCode> DeleteChatBan(string id)
        {
            throw new NotImplementedException();
        }

        public Task<List<GlobalChatBan>> GetChatBans()
        {
            throw new NotImplementedException();
        }

        public Task<List<ProxiesResponse>> GetProxies()
        {
            throw new NotImplementedException();
        }

        public Task<FloProxies> GetProxiesFor(string battleTag)
        {
            throw new NotImplementedException();
        }

        public Task<HttpStatusCode> PutChatBan(ChatBanPutDto chatBan)
        {
            throw new NotImplementedException();
        }

        public Task PutPortraits(PortraitsRequest portraitRequest)
        {
            throw new NotImplementedException();
        }

        public Task<List<string>> SearchSmurfsFor(string battleTag)
        {
            throw new NotImplementedException();
        }

        public Task<ProxyUpdate> UpdateProxies(ProxyUpdate proxyUpdateData, string battleTag)
        {
            throw new NotImplementedException();
        }
    }
}
