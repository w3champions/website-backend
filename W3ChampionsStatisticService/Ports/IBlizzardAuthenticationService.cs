using System.Threading.Tasks;
using W3ChampionsStatisticService.Authorization;

namespace W3ChampionsStatisticService.Ports
{
    public interface IBlizzardAuthenticationService
    {
        Task<BlizzardUserInfo> GetUser(string bearer);
        Task<BlizzardToken> GetToken(string code, string redirectUri);
    }
}