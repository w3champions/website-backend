using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.Chats;
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
        private readonly ChatAuthenticationService _chatAuthenticationService;
        private readonly PersonalSettingsCommandHandler _commandHandler;

        public PersonalSettingsController(
            IBlizzardAuthenticationService authenticationService,
            IPersonalSettingsRepository personalSettingsRepository,
            IPlayerRepository playerRepository,
            ChatAuthenticationService chatAuthenticationService,
            PersonalSettingsCommandHandler commandHandler)
        {
            _authenticationService = authenticationService;
            _personalSettingsRepository = personalSettingsRepository;
            _playerRepository = playerRepository;
            _chatAuthenticationService = chatAuthenticationService;
            _commandHandler = commandHandler;
        }

        [HttpGet("{battleTag}")]
        public async Task<IActionResult> GetPersonalSetting(string battleTag)
        {
            var setting = await _personalSettingsRepository.Load(battleTag);
            if (setting == null)
            {
                var player = await _playerRepository.LoadPlayerProfile(battleTag);
                return Ok(new PersonalSetting(battleTag) { Players = new List<PlayerOverallStats> { player }});
            }
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

        [HttpPut("{battleTag}/api-key")]
        public async Task<IActionResult> SetApiKey(
            string battleTag,
            [FromQuery] string authentication)
        {
            var userInfo = await _authenticationService.GetUser(authentication);
            if (userInfo == null || !battleTag.StartsWith(userInfo.battletag))
            {
                return Unauthorized("Sorry H4ckerb0i");
            }

            var chatUser = await _chatAuthenticationService.GetUserByBattleTag(userInfo.battletag) ?? new ChatUser(battleTag);
            chatUser.CreatApiKey();
            await _chatAuthenticationService.SaveUser(chatUser);

            return Ok(chatUser);
        }

        [HttpGet("{battleTag}/api-key")]
        public async Task<IActionResult> GetApiKey(
            string battleTag,
            [FromQuery] string authentication)
        {
            var userInfo = await _authenticationService.GetUser(authentication);
            if (userInfo == null || !battleTag.StartsWith(userInfo.battletag))
            {
                return Unauthorized("Sorry H4ckerb0i");
            }

            var chatUser = await _chatAuthenticationService.GetUserByBattleTag(userInfo.battletag);

            return Ok(chatUser);
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