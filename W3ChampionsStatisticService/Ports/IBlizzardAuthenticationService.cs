using System.Threading.Tasks;
using W3ChampionsStatisticService.Authorization;

namespace W3ChampionsStatisticService.Ports
{
    public interface IBlizzardAuthenticationService
    {
        Task<BlizzardUserInfo> GetUser(string bearer);
        Task<OAuthToken> GetToken(string code, string redirectUri);
    }
}