using Microsoft.AspNetCore.Http;
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

    /// <summary>
    /// Forwards the admin's multipart body to update-service as a stream, untouched. The battleTag parameter (filled in
    /// by the permission filter) makes MVC bind arguments, and the form value provider would otherwise read the whole
    /// multipart body during binding — buffered, and before the permission filter has run — leaving nothing to forward;
    /// <see cref="DisableFormValueModelBindingAttribute"/> keeps binding off the body, as on the temporary upload.
    /// <para>
    /// <see cref="TemporaryMapUploadBodyLimitAttribute"/> applies here exactly as on the temporary upload, both of its
    /// effects: the per-request ceiling is raised to <see cref="TemporaryMapLimits.TransportBodyBytes"/> — the size
    /// update-service accepts, so a file between the global Kestrel limit (128 MiB, unchanged elsewhere) and the 256 MiB
    /// map cap is no longer refused here with a 413 — and the minimum body data rate is set, so a body that stalls
    /// fails the forward instead of holding the connection. Not <c>[RequestFormLimits]</c>: nothing here form-binds.
    /// </para>
    /// <para>
    /// Ordering: <see cref="BearerHasPermissionFilter"/> is an action filter, so on this action the ceiling and the
    /// floor are in place before its 401 (on the temporary routes the authorization filter's 401 comes first). That is
    /// bounded because no body byte is read before the 401 — nothing ahead of the action reads the body, which
    /// <c>TemporaryMapsControllerPipelineTests.TheAdminMapFilePassthrough_LeavesItsBodyToTheAction_SoModelBindingReadsNoByte</c>
    /// pins (it is the tripwire for any filter or binder added here) — and Kestrel drains an unread body for at most
    /// its 5 s <c>RequestBodyDrainTimeout</c> after the response.
    /// </para>
    /// </summary>
    [HttpPost("{id}/files")]
    [BearerHasPermissionFilter(Permission = EPermission.Maps)]
    [DisableFormValueModelBinding]
    [TemporaryMapUploadBodyLimit]
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
        catch (TaskCanceledException ex)
        {
            // HttpClient's own timeout (the forward takes no request token, so nothing else cancels it).
            return UpstreamTimeout(ex);
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
    /// Answers and logs like HttpRequestExceptionFilter (the error status, or 502 when there is none or it is below 400;
    /// a transport failure's own message names the upstream host and is replaced) while keeping these actions'
    /// plain-text body.
    /// </summary>
    private ObjectResult UpstreamFailure(HttpRequestException ex, [CallerMemberName] string action = "")
    {
        HttpRequestExceptionFilter.LogFailure(_logger, ex, action);
        return StatusCode(HttpRequestExceptionFilter.StatusCodeOf(ex), HttpRequestExceptionFilter.ClientMessageOf(ex));
    }

    /// <summary>
    /// A forward that outlived the client's timeout is answered like a transport failure (502, the same fixed text),
    /// as the temporary upload answers it; the exception is logged, its message naming only the timeout.
    /// </summary>
    private ObjectResult UpstreamTimeout(TaskCanceledException ex, [CallerMemberName] string action = "")
    {
        _logger.LogError(ex, "{Action} timed out forwarding to an upstream service and answered {StatusCode}",
            action, StatusCodes.Status502BadGateway);
        return StatusCode(StatusCodes.Status502BadGateway, HttpRequestExceptionFilter.TransportFailureMessage);
    }

    [HttpGet("tournaments")]
    public async Task<IActionResult> GetTournamentMaps()
    {
        var maps = await _matchmakingServiceClient.GetTournamentMaps();
        // Anonymous route: re-serve only the public map fields, never the admin-only ones MapContract carries.
        return Ok(PublicMapsResponse.From(maps));
    }
}
