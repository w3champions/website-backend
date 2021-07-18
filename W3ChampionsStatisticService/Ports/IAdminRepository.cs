using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Admin;

namespace W3ChampionsStatisticService.Ports
{
    public interface IAdminRepository
    {
        Task<List<ProxiesResponse>> GetProxies();
        Task<ProxyUpdate> UpdateProxies(ProxyUpdate proxyUpdateData, string battleTag);
        Task<FloProxies> GetProxiesFor(string battleTag);
        Task<List<string>> SearchSmurfsFor(string battleTag);
    }
}