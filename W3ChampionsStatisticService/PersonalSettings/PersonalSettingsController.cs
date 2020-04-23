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

        public PersonalSettingsController(
            IBlizzardAuthenticationService authenticationService,
            IPersonalSettingsRepository personalSettingsRepository)
        {
            _authenticationService = authenticationService;
            _personalSettingsRepository = personalSettingsRepository;
        }

        [HttpGet("{battleTag}")]
        public async Task<IActionResult> GetPersonalSetting(string battleTag)
        {
            var setting = await _personalSettingsRepository.Load(battleTag) ?? new PersonalSetting(battleTag);
            return Ok(setting);
        }

        [HttpPut("{battleTag}/profile-message")]
        public async Task<IActionResult> SetProfileMessage(
            string battleTag,
            [FromQuery] string authentication,
            [FromBody] ProfileCommand command)
        {
            var userInfo = await _authenticationService.GetUser(authentication);
            if (userInfo == null || !battleTag.StartsWith(userInfo.battletag))
            {
                return Unauthorized("Sorry H4ckerb0i");
            }

            var setting = await _personalSettingsRepository.Load(battleTag) ?? new PersonalSetting(battleTag);
            setting.ProfileMessage = command.Value;
            await _personalSettingsRepository.Save(setting);

            return Ok();
        }

        [HttpPut("{battleTag}/home-page")]
        public async Task<IActionResult> SetHomePage(
            string battleTag,
            [FromQuery] string authentication,
            [FromBody] ProfileCommand command)
        {
            var userInfo = await _authenticationService.GetUser(authentication);
            if (userInfo == null || !battleTag.StartsWith(userInfo.battletag))
            {
                return Unauthorized("Sorry H4ckerb0i");
            }

            var setting = await _personalSettingsRepository.Load(battleTag) ?? new PersonalSetting(battleTag);
            setting.HomePage = command.Value;
            await _personalSettingsRepository.Save(setting);

            return Ok();
        }

        [HttpPut("{battleTag}/profile-picture")]
        public async Task<IActionResult> SetProfilePicture(
            string battleTag,
            [FromQuery] string authentication,
            [FromBody] SetPictureCommand command)
        {
            var userInfo = await _authenticationService.GetUser(authentication);
            if (userInfo == null || !battleTag.StartsWith(userInfo.battletag))
            {
                return Unauthorized("Sorry H4ckerb0i");
            }

            var setting = await _personalSettingsRepository.Load(battleTag) ?? new PersonalSetting(battleTag);

            var result = setting.SetProfilePicture(command.Race, command.PictureId);
            if (!result) return BadRequest();

            await _personalSettingsRepository.Save(setting);

            return Ok();
        }
    }
}