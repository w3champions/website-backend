using System.Threading.Tasks;
using W3ChampionsStatisticService.Authorization;

namespace W3ChampionsStatisticService.Ports
{
    public interface ITwitchAuthenticationService
    {
        Task<OAuthToken> GetToken();
    }
}