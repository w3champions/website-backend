using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.Rewards.Portraits;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.PersonalSettings;

[ApiController]
[Route("api/personal-settings")]
public class PersonalSettingsController(
    IPersonalSettingsRepository personalSettingsRepository,
    PortraitCommandHandler commandHandler) : ControllerBase
{
    private readonly IPersonalSettingsRepository _personalSettingsRepository = personalSettingsRepository;
    private readonly PortraitCommandHandler _commandHandler = commandHandler;

    [HttpGet("{battleTag}")]
    public async Task<IActionResult> GetPersonalSetting(string battleTag)
    {
        try
        {
            PersonalSetting setting = await _personalSettingsRepository.Find(battleTag);
            return setting == null ? NotFound($"Personal settings of {battleTag} not found.") : Ok(setting);
        }
        catch
        {
            return StatusCode(503, "Failed to load personal settings.");
        }
    }

    [HttpGet("{commaSeparatedBattleTags}/many")]
    public async Task<IActionResult> GetPersonalSettings(string commaSeparatedBattleTags)
    {
        var splitBattleTags = commaSeparatedBattleTags.Split(new string[] { "," }, System.StringSplitOptions.RemoveEmptyEntries);

        var settings = await _personalSettingsRepository.LoadMany(splitBattleTags);

        if (settings != null)
        {
            return Ok(settings.Select(x => new
            {
                x.Id,
                x.CountryCode,
                x.Location,
                x.ProfilePicture
            }));
        }

        return Ok(new object[0]);
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
    [BearerCheckIfBattleTagBelongsToAuth]
    public async Task<IActionResult> SetProfilePicture(
        string battleTag,
        [FromBody] SetPictureCommand command)
    {
        var result = await _commandHandler.UpdatePicture(battleTag, command);

        if (!result) return BadRequest();

        return Ok();
    }
}
