using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.WebApiCompatShim;
using System.Threading.Tasks;
using Serilog;
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
    UpdateServiceClient updateServiceClient,
    MapMetadataRecomputeService mapMetadataRecomputeService) : ControllerBase
{
    private readonly MatchmakingServiceClient _matchmakingServiceClient = matchmakingServiceClient;
    private readonly UpdateServiceClient _updateServiceClient = updateServiceClient;
    private readonly MapMetadataRecomputeService _mapMetadataRecomputeService = mapMetadataRecomputeService;

    public const string RecomputeUpstreamError = "A sibling service failed; matchmaking was not patched. Rerun a dry run to see the current state.";

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

    /// <summary>
    /// Recomputes every matchmaking map's checksum/crc32 on update-service and, unless it is a dry run (the
    /// default, also for an empty body), patches the corrected values into matchmaking. 502 when a sibling
    /// service fails: before any patch when matchmaking's map list or any update-service chunk fails (matchmaking
    /// is only patched from complete data; a rerun finishes the job), with the full report when only a
    /// matchmaking patch call failed.
    /// </summary>
    [HttpPost("metadata/recompute")]
    [BearerHasPermissionFilter(Permission = EPermission.Maps)]
    public async Task<IActionResult> RecomputeMapMetadata([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] MapMetadataRecomputeBody body)
    {
        if (body?.MapIds is { Length: 0 })
        {
            return BadRequest(new { error = "mapIds must not be empty; omit it to process every map" });
        }

        try
        {
            var report = await _mapMetadataRecomputeService.Recompute(body?.DryRun ?? true, body?.MapIds);
            return report.MmPatchError == null ? Ok(report) : StatusCode(StatusCodes.Status502BadGateway, report);
        }
        catch (MapMetadataRecomputeUpstreamException ex)
        {
            Log.Error(ex, "Map metadata recompute failed at {Service} (status {UpstreamStatus}): {Message}", ex.Service, ex.UpstreamStatus, ex.Message);
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = RecomputeUpstreamError,
                service = ex.Service,
                upstreamStatus = ex.UpstreamStatus,
                reason = ex.Reason,
                pathsCompleted = ex.PathsCompleted,
                pathsTotal = ex.PathsTotal,
                matchmakingPatched = false,
            });
        }
    }

    [HttpGet("tournaments")]
    public async Task<IActionResult> GetTournamentMaps()
    {
        var maps = await _matchmakingServiceClient.GetTournamentMaps();
        return Ok(maps);
    }
}
