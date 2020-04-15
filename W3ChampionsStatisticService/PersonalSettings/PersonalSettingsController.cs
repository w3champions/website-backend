using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.Authorization;

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

        [HttpPut("profile-message")]
        public async Task<IActionResult> ResetAllReadModels(
            [FromQuery] string authentication,
            [FromBody] ProfileMessageCommand command)
        {
            var userInfo = await _authenticationService.GetUser(authentication);
            if (userInfo == null)
            {
                return Unauthorized("Sorry H4ckerb0i");
            }

            var setting = await _personalSettingsRepository.Load(userInfo.battletag);
            setting.ProfileMessage = command.Message;
            await _personalSettingsRepository.Save(setting);

            return Ok();
        }

        [HttpPut("profile-picture")]
        public async Task<IActionResult> ResetAllReadModels(
            [FromQuery] string authentication,
            [FromBody] ProfilePictureCommand command)
        {
            var userInfo = await _authenticationService.GetUser(authentication);
            if (userInfo == null)
            {
                return Unauthorized("Sorry H4ckerb0i");
            }

            var setting = await _personalSettingsRepository.Load(userInfo.battletag);
            setting.ProfilePicture = command.Picture;
            await _personalSettingsRepository.Save(setting);

            return Ok();
        }
    }
}