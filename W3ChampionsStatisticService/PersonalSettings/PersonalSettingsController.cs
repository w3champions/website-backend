using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.Rewards.Portraits;
using W3ChampionsStatisticService.Services;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.PersonalSettings;

[ApiController]
[Route("api/personal-settings")]
[Trace]
public class PersonalSettingsController(
    IPersonalSettingsRepository personalSettingsRepository,
    PortraitCommandHandler commandHandler,
    IBattleTagResolver battleTagResolver) : ControllerBase
{
    private readonly IPersonalSettingsRepository _personalSettingsRepository = personalSettingsRepository;
    private readonly PortraitCommandHandler _commandHandler = commandHandler;
    private readonly IBattleTagResolver _battleTagResolver = battleTagResolver;

    [HttpGet("{battleTag}")]
    public async Task<IActionResult> GetPersonalSetting(string battleTag)
    {
        try
        {
            var canonical = await _battleTagResolver.ResolveCanonical(battleTag);
            if (canonical == null)
                return NotFound();

            // Load (not Find) so that RaceWins is populated from PlayerOverallStats,
            // preserving the join that the original implementation provided.
            // Returns null when the document doesn't exist yet; we hand back a
            // default PersonalSetting in that case without writing to the DB.
            var settings = await _personalSettingsRepository.Load(canonical);
            return Ok(settings ?? new PersonalSetting(canonical));
        }
        catch
        {
            return StatusCode(503, "Failed to load personal settings.");
        }
    }

    [HttpGet("{commaSeparatedBattleTags}/many")]
    public async Task<IActionResult> GetPersonalSettings(string commaSeparatedBattleTags)
    {
        var splitBattleTags = commaSeparatedBattleTags.Split([","], System.StringSplitOptions.RemoveEmptyEntries);

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
    [BearerCheckIfBattleTagBelongsToAuth]
    public async Task<IActionResult> SetPersonalSetting(
        string battleTag,
        [FromBody] PersonalSettingsDTO dto)
    {
        var setting = await _personalSettingsRepository.LoadOrCreate(battleTag) ?? new PersonalSetting(battleTag);

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
