using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3C.Contracts.Admin.Permission;
using W3ChampionsStatisticService.Rewards.Portraits;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.Rewards.Controllers;

public class ReplacePortraitRequest
{
    [Range(1, int.MaxValue)]
    public int NewPortraitId { get; set; }
}

[ApiController]
[Route("api/rewards/portrait-replacements")]
public class PortraitReplacementController(PortraitReplacementService replacements) : ControllerBase
{
    [HttpGet("/api/rewards/portraits/{oldId:int}/owners")]
    [BearerHasPermissionFilter(Permission = EPermission.Content)]
    public async Task<IActionResult> Owners(int oldId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, [FromQuery] string search = null)
    {
        try { return Ok(await replacements.Owners(oldId, page, pageSize, search)); }
        catch (ValidationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("/api/rewards/portraits/asset-statuses")]
    [BearerHasPermissionFilter(Permission = EPermission.Content)]
    public async Task<IActionResult> AssetStatuses([FromBody] List<int> portraitIds)
    {
        try { return Ok(await replacements.AssetStatuses(portraitIds)); }
        catch (ValidationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("{oldId:int}")]
    [BearerHasPermissionFilter(Permission = EPermission.Content)]
    public async Task<IActionResult> Preview(int oldId)
    {
        try { return Ok(await replacements.Preview(oldId)); }
        catch (ValidationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("{oldId:int}")]
    [BearerHasPermissionFilter(Permission = EPermission.Content)]
    public async Task<IActionResult> Replace(int oldId, [FromBody] ReplacePortraitRequest request)
    {
        try { return Ok(await replacements.Replace(oldId, request.NewPortraitId)); }
        catch (ValidationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (FileNotFoundException) { return BadRequest(new { message = "The replacement image must be uploaded to S3 before assigning it." }); }
    }
}
