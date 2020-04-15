using System.Threading.Tasks;

namespace W3ChampionsStatisticService.Authorization
{
    public interface IBlizzardAuthenticationService
    {
        Task<BlizzardUserInfo> GetUser(string bearer);
        Task<BlizzardToken> GetToken(string code, string redirectUri);
    }
}