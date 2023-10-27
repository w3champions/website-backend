using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using System.Net.Http;
using W3C.Contracts.Admin.Permission;

namespace W3ChampionsStatisticService.Admin.Permissions;

[ApiController]
[Route("api/admin/permissions")]
public class PermissionsController : ControllerBase
{
    private readonly IPermissionsRepository _permissionsRepository;

    public PermissionsController(
        IPermissionsRepository permissionsRepository)
    {
        _permissionsRepository = permissionsRepository;
    }

    [HttpGet]
    [HasPermissionFilter(Permission = EPermission.Permissions)]
    public async Task<IActionResult> GetPermissions([FromQuery] string authorization)
    {
        try {
            var permissions = await _permissionsRepository.GetPermissions(authorization);
            return Ok(permissions);
        } catch (HttpRequestException ex) {
            return StatusCode((int)ex.StatusCode, ex.Message);
        }
    }

    [HttpPost("add")]
    [HasPermissionFilter(Permission = EPermission.Permissions)]
    public async Task<IActionResult> AddAdmin([FromBody] Permission permission, [FromQuery] string authorization)
    {
        try {
            await _permissionsRepository.AddAdmin(permission, authorization);
            return Ok();
        } catch (HttpRequestException ex) {
            return StatusCode((int)ex.StatusCode, ex.Message);
        }
    }

    [HttpPut("edit")]
    [HasPermissionFilter(Permission = EPermission.Permissions)]
    public async Task<IActionResult> EditAdmin([FromBody] Permission permission, [FromQuery] string authorization)
    {
        try {
            await _permissionsRepository.EditAdmin(permission, authorization);
            return Ok();
        } catch (HttpRequestException ex) {
            return StatusCode((int)ex.StatusCode, ex.Message);
        }
    }

    [HttpDelete("delete")]
    [HasPermissionFilter(Permission = EPermission.Permissions)]
    public async Task<IActionResult> DeleteAdmin([FromQuery] string id, string authorization)
    {
        try {
            await _permissionsRepository.DeleteAdmin(id, authorization);
            return Ok();
        } catch (HttpRequestException ex) {
            return StatusCode((int)ex.StatusCode, ex.Message);
        }
    }
}
