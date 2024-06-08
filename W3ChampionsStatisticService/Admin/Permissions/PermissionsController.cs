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
    [BearerHasPermissionFilter(Permission = EPermission.Permissions)]
    public async Task<IActionResult> GetPermissions(string token)
    {
        try {
            var permissions = await _permissionsRepository.GetPermissions(token);
            return Ok(permissions);
        } catch (HttpRequestException ex) {
            return StatusCode((int)ex.StatusCode, ex.Message);
        }
    }

    [HttpPost("add")]
    [BearerHasPermissionFilter(Permission = EPermission.Permissions)]
    public async Task<IActionResult> AddAdmin([FromBody] Permission permission, string token)
    {
        try {
            await _permissionsRepository.AddAdmin(permission, token);
            return Ok();
        } catch (HttpRequestException ex) {
            return StatusCode((int)ex.StatusCode, ex.Message);
        }
    }

    [HttpPut("edit")]
    [BearerHasPermissionFilter(Permission = EPermission.Permissions)]
    public async Task<IActionResult> EditAdmin([FromBody] Permission permission, string token)
    {
        try {
            await _permissionsRepository.EditAdmin(permission, token);
            return Ok();
        } catch (HttpRequestException ex) {
            return StatusCode((int)ex.StatusCode, ex.Message);
        }
    }

    [HttpDelete("delete")]
    [BearerHasPermissionFilter(Permission = EPermission.Permissions)]
    public async Task<IActionResult> DeleteAdmin([FromQuery] string id, string token)
    {
        try {
            await _permissionsRepository.DeleteAdmin(id, token);
            return Ok();
        } catch (HttpRequestException ex) {
            return StatusCode((int)ex.StatusCode, ex.Message);
        }
    }
}
