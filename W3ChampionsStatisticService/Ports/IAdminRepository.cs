using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Admin;

namespace W3ChampionsStatisticService.Ports
{
    public interface IAdminRepository
    {
        Task<List<ProxiesResponse>> GetProxies();
        Task<List<ProxiesResponse>> UpdateProxy(List<ProxyUpdate> proxyUpdateData, string battleTag);
        Task<List<ProxiesResponse>> GetProxiesFor(string battleTag);
    }
}