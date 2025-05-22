﻿using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using W3C.Contracts.Admin.Permission;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.Rewards.Portraits;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Rewards;

[Route("api/rewards")]
[ApiController]
[Trace]
public class RewardsController(
    IPortraitRepository portraitRepository,
    PortraitCommandHandler portraitCommandHandler) : ControllerBase
{
    private readonly PortraitCommandHandler _portraitCommandHandler = portraitCommandHandler;
    private readonly IPortraitRepository _portraitRepository = portraitRepository;

    [HttpPut("portraits")]
    [BearerHasPermissionFilter(Permission = EPermission.Content)]
    public async Task<IActionResult> PutPortraits([FromBody] PortraitsCommand command)
    {
        await _portraitCommandHandler.UpsertSpecialPortraits(command);
        return Ok();
    }

    [HttpDelete("portraits")]
    [BearerHasPermissionFilter(Permission = EPermission.Content)]
    public async Task<IActionResult> DeletePortraits([FromBody] PortraitsCommand command)
    {
        await _portraitCommandHandler.DeleteSpecialPortraits(command);
        return Ok();
    }

    [HttpGet("portrait-definitions")]
    [BearerHasPermissionFilter(Permission = EPermission.Content)]
    public async Task<IActionResult> GetPortraitDefinitions()
    {
        var portraits = await _portraitCommandHandler.GetPortraitDefinitions();
        return Ok(portraits);
    }

    [HttpPost("portrait-definitions")]
    [BearerHasPermissionFilter(Permission = EPermission.Content)]
    public async Task<IActionResult> CreatePortraits([FromBody] PortraitsDefinitionCommand command)
    {
        await _portraitCommandHandler.AddPortraitDefinitions(command);
        return Ok();
    }

    [HttpPut("portrait-definitions")]
    [BearerHasPermissionFilter(Permission = EPermission.Content)]
    public async Task<IActionResult> DefinePortraits([FromBody] PortraitsDefinitionCommand command)
    {
        await _portraitCommandHandler.UpdatePortraitDefinitions(command);
        return Ok();
    }

    [HttpDelete("portrait-definitions")]
    [BearerHasPermissionFilter(Permission = EPermission.Content)]
    public async Task<IActionResult> RemovePortraits([FromBody] PortraitsDefinitionCommand command)
    {
        await _portraitCommandHandler.RemovePortraitDefinitions(command);
        return Ok();
    }

    [HttpGet("portrait-groups")]
    public async Task<IActionResult> GetPortraitGroups()
    {
        var portraitGroups = await _portraitRepository.LoadDistinctPortraitGroups();
        return Ok(portraitGroups);
    }
}
