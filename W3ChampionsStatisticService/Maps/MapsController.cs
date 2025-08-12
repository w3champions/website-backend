using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.WebApiCompatShim;
using System.Threading.Tasks;
using W3C.Contracts.Matchmaking;
using W3C.Domain.MatchmakingService;
using W3C.Domain.UpdateService;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using System.Net.Http;
using W3C.Contracts.Admin.Permission;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Maps;

[ApiController]
[Route("api/maps")]
[Trace]
public class MapsController(
    MatchmakingServiceClient matchmakingServiceClient,
    UpdateServiceClient updateServiceClient) : ControllerBase
{
    private readonly MatchmakingServiceClient _matchmakingServiceClient = matchmakingServiceClient;
    private readonly UpdateServiceClient _updateServiceClient = updateServiceClient;

    [HttpGet("")]
    [BearerHasPermissionFilter(Permission = EPermission.Maps)]
    public async Task<IActionResult> GetMaps([FromQuery] GetMapsRequest request)
    {
        var maps = await _matchmakingServiceClient.GetMaps(request);
        return Ok(maps);
    }

    [HttpPost("")]
    [BearerHasPermissionFilter(Permission = EPermission.Maps)]
    public async Task<IActionResult> CreateMap([FromBody] MapContract request)
    {
        try
        {
            var map = await _matchmakingServiceClient.CreateMap(request);
            return Ok(map);
        }
        catch (HttpRequestException ex)
        {
            return StatusCode((int)ex.StatusCode, ex.Message);
        }
    }

    [HttpPut("{id}")]
    [BearerHasPermissionFilter(Permission = EPermission.Maps)]
    public async Task<IActionResult> UpdateMap(int id, [FromBody] MapContract request)
    {
        try
        {
            var map = await _matchmakingServiceClient.UpdateMap(id, request);
            return Ok(map);
        }
        catch (HttpRequestException ex)
        {
            return StatusCode((int)ex.StatusCode, ex.Message);
        }
    }

    [HttpGet("{id}/files")]
    [BearerHasPermissionFilter(Permission = EPermission.Maps)]
    public async Task<IActionResult> GetMapFiles(int id)
    {
        try
        {
            var mapFiles = await _updateServiceClient.GetMapFiles(id);
            return Ok(mapFiles);
        }
        catch (HttpRequestException ex)
        {
            return StatusCode((int)ex.StatusCode, ex.Message);
        }
    }

    [HttpPost("{id}/files")]
    [BearerHasPermissionFilter(Permission = EPermission.Maps)]
    public async Task<IActionResult> CreateMapFile()
    {
        try
        {
            HttpRequestMessageFeature hreqmf = new(Request.HttpContext);
            var map = await _updateServiceClient.CreateMapFromFormAsync(hreqmf.HttpRequestMessage);
            return Ok(map);
        }
        catch (HttpRequestException ex)
        {
            return StatusCode((int)ex.StatusCode, ex.Message);
        }
    }

    [HttpGet("files/{fileId}")]
    [BearerHasPermissionFilter(Permission = EPermission.Maps)]
    public async Task<IActionResult> GetMapFile(string fileId)
    {
        try
        {
            var mapFile = await _updateServiceClient.GetMapFile(fileId);
            return Ok(mapFile);
        }
        catch (HttpRequestException ex)
        {
            return StatusCode((int)ex.StatusCode, ex.Message);
        }
    }

    [HttpDelete("files/{fileId}")]
    [BearerHasPermissionFilter(Permission = EPermission.Maps)]
    public async Task<IActionResult> DeleteMapFile(string fileId)
    {
        try
        {
            await _updateServiceClient.DeleteMapFile(fileId);
            return NoContent();
        }
        catch (HttpRequestException ex)
        {
            return StatusCode((int)ex.StatusCode, ex.Message);
        }
    }

    [HttpGet("tournaments")]
    public async Task<IActionResult> GetTournamentMaps()
    {
        var maps = await _matchmakingServiceClient.GetTournamentMaps();
        return Ok(maps);
    }
}
