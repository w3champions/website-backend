using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.PersonalSettings
{
    [ApiController]
    [Route("api/personal-settings")]
    public class PersonalSettingsController : ControllerBase
    {
        private readonly IPersonalSettingsRepository _personalSettingsRepository;
        private readonly IPlayerRepository _playerRepository;
        private readonly PersonalSettingsCommandHandler _commandHandler;

        public PersonalSettingsController(
            IPersonalSettingsRepository personalSettingsRepository,
            IPlayerRepository playerRepository,
            PersonalSettingsCommandHandler commandHandler)
        {
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

        [HttpGet("{commaSeparatedBattleTags}/many")]
        public async Task<IActionResult> GetPersonalSettings(string commaSeparatedBattleTags)
        {
            var splitBattleTags = commaSeparatedBattleTags.Split(new string[] { "," }, System.StringSplitOptions.RemoveEmptyEntries);

            var settings = await _personalSettingsRepository.LoadMany(splitBattleTags);

            if (settings != null)
            {
                return Ok(settings.Select(x => new {
                    x.Id,
                    x.CountryCode,
                    x.Location,
                    x.ProfilePicture
                }));
            }

            return Ok(new object[0]);
        }

        [HttpPost("populate-country-codes")]
        public async Task<IActionResult> MigrateCountry()
        {
            var settings = await _personalSettingsRepository.LoadAll();
            var countriesJson = System.IO.File.ReadAllText("countries.json");
            var countries = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(countriesJson);

            foreach (var setting in settings)
            {
                if (!string.IsNullOrEmpty(setting.Country) && countries.ContainsKey(setting.Country))
                {
                    var foundCountryCode = countries[setting.Country];
                    setting.CountryCode = foundCountryCode;
                    await _personalSettingsRepository.Save(setting);
                }
            }

            return Ok();
        }

        [HttpPut("{battleTag}")]
        public async Task<IActionResult> SetPersonalSetting(
           string battleTag,
           [FromBody] PersonalSettingsDTO dto)
        {
            var setting = await _personalSettingsRepository.Load(battleTag) ?? new PersonalSetting(battleTag);

            setting.Update(dto);

            await _personalSettingsRepository.Save(setting);

            return Ok();
        }

        [HttpPut("{battleTag}/profile-picture")]
        [CheckIfBattleTagBelongsToAuthCode]
        public async Task<IActionResult> SetProfilePicture(
            string battleTag,
            [FromBody] SetPictureCommand command)
        {
            var result = await _commandHandler.UpdatePicture(battleTag, command);

            if (!result) return BadRequest();

            return Ok();
        }

        [HttpPost("{battleTag}/upvote")]
        public async Task<int> UpvotePlayer(string battleTag)
        {
            var result = await _personalSettingsRepository.Upvote(battleTag);

            return result.Upvotes;
        }
    }
}