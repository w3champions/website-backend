using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.PlayerProfiles;
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
        private readonly PersonalSettingsCommandHandler _commandHandler;

        public PersonalSettingsController(
            IBlizzardAuthenticationService authenticationService,
            IPersonalSettingsRepository personalSettingsRepository,
            IPlayerRepository playerRepository,
            PersonalSettingsCommandHandler commandHandler)
        {
            _authenticationService = authenticationService;
            _personalSettingsRepository = personalSettingsRepository;
            _playerRepository = playerRepository;
            _commandHandler = commandHandler;
        }

        [HttpGet("{battleTag}")]
        public async Task<IActionResult> GetPersonalSetting(string battleTag)
        {
            var setting = await _personalSettingsRepository.Load(battleTag);
            if (setting == null)
            {
                var player = await _playerRepository.LoadPlayerProfile(battleTag);
                return Ok(new PersonalSetting(battleTag) { Players = new List<PlayerOverallStats> { player } });
            }
            return Ok(setting);
        }

        [HttpPut("{battleTag}")]
        public async Task<IActionResult> SetPersonalSetting(
           string battleTag,
           [FromQuery] string authentication,
           [FromBody] PersonalSettingsDTO dto)
        {
            var userInfo = await _authenticationService.GetUser(authentication);
            if (userInfo == null || !battleTag.StartsWith(userInfo.battletag))
            {
                return Unauthorized("Sorry H4ckerb0i");
            }

            var setting = await _personalSettingsRepository.Load(battleTag) ?? new PersonalSetting(battleTag);
            setting.ProfileMessage = dto.ProfileMessage != null ? dto.ProfileMessage : setting.ProfileMessage;
            setting.Twitch = dto.Twitch != null ? dto.Twitch : setting.Twitch;
            setting.YouTube = dto.Youtube != null ? dto.Twitch : setting.Twitch;
            setting.Twitter = dto.Twitter != null ? dto.Twitter : setting.Twitter;
            setting.HomePage = dto.HomePage != null ? dto.HomePage : setting.HomePage;
            setting.Country = dto.Country != null ? dto.Country : setting.Country;

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

            var result = await _commandHandler.UpdatePicture(battleTag, command);

            if (!result) return BadRequest();

            return Ok();
        }
    }
}