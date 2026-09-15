using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.WebApiCompatShim;
using System.Threading.Tasks;
using W3C.Contracts.Matchmaking;
using W3C.Domain.MatchmakingService;
using W3C.Domain.UpdateService;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using W3ChampionsStatisticService.WebApi.ExceptionFilters;
using System.Net.Http;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using W3C.Contracts.Admin.Permission;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Maps;

[ApiController]
[Route("api/maps")]
[Trace]
public class MapsController(
    MatchmakingServiceClient matchmakingServiceClient,
    UpdateServiceClient updateServiceClient,
    ILogger<MapsController> logger) : ControllerBase
{
    private readonly MatchmakingServiceClient _matchmakingServiceClient = matchmakingServiceClient;
    private readonly UpdateServiceClient _updateServiceClient = updateServiceClient;
    private readonly ILogger<MapsController> _logger = logger;

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
            return UpstreamFailure(ex);
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
            return UpstreamFailure(ex);
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
            return UpstreamFailure(ex);
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
            return UpstreamFailure(ex);
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
            return UpstreamFailure(ex);
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
            return UpstreamFailure(ex);
        }
    }

    /// <summary>
    /// Answers and logs like HttpRequestExceptionFilter (the status, or 500 for a transport failure, whose own message
    /// names the upstream host and is replaced) while keeping these actions' plain-text body.
    /// </summary>
    private ObjectResult UpstreamFailure(HttpRequestException ex, [CallerMemberName] string action = "")
    {
        HttpRequestExceptionFilter.LogFailure(_logger, ex, action);
        return StatusCode(HttpRequestExceptionFilter.StatusCodeOf(ex), HttpRequestExceptionFilter.ClientMessageOf(ex));
    }

    [HttpGet("tournaments")]
    public async Task<IActionResult> GetTournamentMaps()
    {
        var maps = await _matchmakingServiceClient.GetTournamentMaps();
        // Anonymous route: re-serve only the public map fields, never the admin-only ones MapContract carries.
        return Ok(PublicMapsResponse.From(maps));
    }
}
