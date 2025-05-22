﻿using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using System.Net.Http;
using W3C.Contracts.Admin.Permission;
using W3ChampionsStatisticService.Services;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Admin.Permissions;

[ApiController]
[Route("api/admin/permissions")]
[Trace]
public class PermissionsController(IdentityServiceClient identityServiceClient) : ControllerBase
{
    private readonly IdentityServiceClient _identityServiceClient = identityServiceClient;

    [HttpGet]
    [InjectAuthToken]
    public async Task<IActionResult> GetPermissions([NoTrace] string authToken)
    {
        try
        {
            var permissions = await _identityServiceClient.GetPermissions(authToken);
            return Ok(permissions);
        }
        catch (HttpRequestException ex)
        {
            return StatusCode((int)ex.StatusCode, ex.Message);
        }
    }

    [HttpPost("add")]
    [InjectAuthToken]
    public async Task<IActionResult> AddAdmin([FromBody] Permission permission, [NoTrace] string authToken)
    {
        try
        {
            await _identityServiceClient.AddAdmin(permission, authToken);
            return Ok();
        }
        catch (HttpRequestException ex)
        {
            return StatusCode((int)ex.StatusCode, ex.Message);
        }
    }

    [HttpPut("edit")]
    [InjectAuthToken]
    public async Task<IActionResult> EditAdmin([FromBody] Permission permission, [NoTrace] string authToken)
    {
        try
        {
            await _identityServiceClient.EditAdmin(permission, authToken);
            return Ok();
        }
        catch (HttpRequestException ex)
        {
            return StatusCode((int)ex.StatusCode, ex.Message);
        }
    }

    [HttpDelete("delete")]
    [InjectAuthToken]
    public async Task<IActionResult> DeleteAdmin([FromQuery] string id, [NoTrace] string authToken)
    {
        try
        {
            await _identityServiceClient.DeleteAdmin(id, authToken);
            return Ok();
        }
        catch (HttpRequestException ex)
        {
            return StatusCode((int)ex.StatusCode, ex.Message);
        }
    }
}
