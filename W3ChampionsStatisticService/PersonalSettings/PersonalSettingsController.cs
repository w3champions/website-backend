using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.Authorization;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.PersonalSettings
{
    [ApiController]
    [Route("api/personal-settings")]
    public class PersonalSettingsController : ControllerBase
    {
        private readonly IBlizzardAuthenticationService _authenticationService;
        private readonly IPersonalSettingsRepository _personalSettingsRepository;
        private readonly IPlayerRepository _playerRepository;

        public PersonalSettingsController(
            IBlizzardAuthenticationService authenticationService,
            IPersonalSettingsRepository personalSettingsRepository,
            IPlayerRepository playerRepository)
        {
            _authenticationService = authenticationService;
            _personalSettingsRepository = personalSettingsRepository;
            _playerRepository = playerRepository;
        }

        [HttpGet("{battleTag}")]
        public async Task<IActionResult> ResetAllReadModels(string battleTag)
        {
            var setting = await _personalSettingsRepository.Load(battleTag) ?? new PersonalSetting(battleTag);
            return Ok(setting);
        }

        [HttpPut("profile-message")]
        public async Task<IActionResult> SetProfileMessage(
            [FromQuery] string authentication,
            [FromBody] ProfileCommand command)
        {
            var userInfo = await _authenticationService.GetUser(authentication);
            if (userInfo == null)
            {
                return Unauthorized("Sorry H4ckerb0i");
            }

            var setting = await _personalSettingsRepository.Load(userInfo.battletag) ?? new PersonalSetting(userInfo.battletag);
            setting.ProfileMessage = command.Value;
            await _personalSettingsRepository.Save(setting);

            return Ok();
        }

        [HttpPut("home-page")]
        public async Task<IActionResult> SetHomePage(
            [FromQuery] string authentication,
            [FromBody] ProfileCommand command)
        {
            var userInfo = await _authenticationService.GetUser(authentication);
            if (userInfo == null)
            {
                return Unauthorized("Sorry H4ckerb0i");
            }

            var setting = await _personalSettingsRepository.Load(userInfo.battletag) ?? new PersonalSetting(userInfo.battletag);
            setting.HomePage = command.Value;
            await _personalSettingsRepository.Save(setting);

            return Ok();
        }

        [HttpPut("profile-picture")]
        public async Task<IActionResult> SetProfilePicture(
            [FromQuery] string authentication,
            [FromBody] SetPictureCommand command)
        {
            var userInfo = await _authenticationService.GetUser(authentication);
            if (userInfo == null)
            {
                return Unauthorized("Sorry H4ckerb0i");
            }

            var player = await _playerRepository.Load($"{userInfo.battletag}@{command.GateWay}");
            var setting = await _personalSettingsRepository.Load(userInfo.battletag);
            var result = setting.SetProfilePicture(player, command.Race, command.PictureId);
            if (!result) return BadRequest();

            await _personalSettingsRepository.Save(setting);

            return Ok();
        }
    }
}