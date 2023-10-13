using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using W3C.Domain.Repositories;
using W3C.Domain.CommonValueObjects;
using System.Net;
using System.Net.Http;
using W3C.Contracts.Admin.Permission;

namespace W3ChampionsStatisticService.Admin
{
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
        [HasPermissionsPermission]
        public async Task<IActionResult> GetPermissions([FromQuery] string authorization)
        {
            var permissions = await _permissionsRepository.GetPermissions(authorization);
            return Ok(permissions);
        }

        [HttpPost("add")]
        [HasPermissionsPermission]
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
        [HasPermissionsPermission]
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
        [HasPermissionsPermission]
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
}
