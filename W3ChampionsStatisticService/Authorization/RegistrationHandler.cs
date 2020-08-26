using System.Threading.Tasks;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Authorization
{
    public class RegistrationHandler
    {
        private readonly IBlizzardAuthenticationService _authenticationService;
        private readonly IPlayerRepository _playerRepository;

        public RegistrationHandler(IBlizzardAuthenticationService authenticationService, IPlayerRepository playerRepository)
        {
            _authenticationService = authenticationService;
            _playerRepository = playerRepository;
        }

        public async Task<BlizzardUserInfo> GetUserOrRegister(string bearer)
        {
            var userInfo = await _authenticationService.GetUser(bearer);

            if (userInfo == null)
            {
                return null;
            }

            var battleTag = userInfo.battletag;
            var profile = await _playerRepository.LoadPlayerProfile(battleTag);
            if (profile == null)
            {
                await _playerRepository.UpsertPlayer(PlayerOverallStats.Create(battleTag));
            }

            return userInfo;
        }
    }
}