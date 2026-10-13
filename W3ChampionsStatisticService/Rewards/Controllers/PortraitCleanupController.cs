using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3C.Contracts.Admin.Permission;
using W3ChampionsStatisticService.Rewards.Portraits;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.Rewards.Controllers;

[ApiController]
[Route("api/rewards/portraits/{id:int}/cleanup")]
public class PortraitCleanupController(PortraitCleanupService cleanup) : ControllerBase
{
    [HttpPost]
    [BearerHasPermissionFilter(Permission = EPermission.Content)]
    public async Task<IActionResult> Cleanup(int id, [FromBody] PortraitCleanupRequest request)
    {
        try { return Ok(await cleanup.Cleanup(id, request)); }
        catch (ValidationException ex) { return BadRequest(new { message = ex.Message }); }
    }
}
