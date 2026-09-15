using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.WebApiCompatShim;
using System.Threading.Tasks;
using W3C.Contracts.Matchmaking;
using W3C.Domain.MatchmakingService;
using W3C.Domain.UpdateService;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using System.Net;
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
    public async Task<IActionResult> CreateMap([FromBody] MapContract request, [NoTrace] string battleTag)
    {
        try
        {
            // The admin who uploaded/edited the map, for the Uploader column on the admin Maps page.
            request.Uploader = battleTag;
            var map = await _matchmakingServiceClient.CreateMap(request);
            return Ok(map);
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(StatusCodeOf(ex), ex.Message);
        }
    }

    [HttpPut("{id}")]
    [BearerHasPermissionFilter(Permission = EPermission.Maps)]
    public async Task<IActionResult> UpdateMap(int id, [FromBody] MapContract request, [NoTrace] string battleTag)
    {
        try
        {
            // The admin who uploaded/edited the map, for the Uploader column on the admin Maps page.
            request.Uploader = battleTag;
            var map = await _matchmakingServiceClient.UpdateMap(id, request);
            return Ok(map);
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(StatusCodeOf(ex), ex.Message);
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
            return StatusCode(StatusCodeOf(ex), ex.Message);
        }
    }

    [HttpPost("{id}/files")]
    [BearerHasPermissionFilter(Permission = EPermission.Maps)]
    public async Task<IActionResult> CreateMapFile([NoTrace] string battleTag)
    {
        try
        {
            HttpRequestMessageFeature hreqmf = new(Request.HttpContext);
            var map = await _updateServiceClient.CreateMapFromFormAsync(hreqmf.HttpRequestMessage, battleTag);
            return Ok(map);
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(StatusCodeOf(ex), ex.Message);
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
            return StatusCode(StatusCodeOf(ex), ex.Message);
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
            return StatusCode(StatusCodeOf(ex), ex.Message);
        }
    }

    /// <summary>
    /// A transport failure (connection refused, DNS, TLS) has no status code; answer 500 for it, as
    /// HttpRequestExceptionFilter does, instead of throwing on the null.
    /// </summary>
    private static int StatusCodeOf(HttpRequestException ex) => (int)(ex.StatusCode ?? HttpStatusCode.InternalServerError);

    [HttpGet("tournaments")]
    public async Task<IActionResult> GetTournamentMaps()
    {
        var maps = await _matchmakingServiceClient.GetTournamentMaps();
        // Anonymous route: re-serve only the public map fields, never the admin-only ones MapContract carries.
        return Ok(PublicMapsResponse.From(maps));
    }
}
