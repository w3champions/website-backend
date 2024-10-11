using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using System.Net.Http;
using System.Net.Mime;
using W3C.Contracts.Admin.Permission;

namespace W3ChampionsStatisticService.Admin.Logs;

[ApiController]
[Route("api/admin/logs")]
public class LogsController(
    ILogsRepository logsRepository) : ControllerBase
{
    private readonly ILogsRepository _logsRepository = logsRepository;

    [HttpGet]
    [BearerHasPermissionFilter(Permission = EPermission.Logs)]
    public async Task<IActionResult> GetLogfileNames()
    {
        try
        {
            var logfileNames = await _logsRepository.GetLogfileNames();
            return Ok(logfileNames);
        }
        catch (HttpRequestException ex)
        {
            int statusCode = ex.StatusCode is null ? 500 : (int)ex.StatusCode;
            return StatusCode(statusCode, ex.Message);
        }
    }

    [HttpGet("{logfileName}")]
    [BearerHasPermissionFilter(Permission = EPermission.Logs)]
    public async Task<IActionResult> GetLogContent([FromRoute] string logfileName)
    {
        try
        {
            var logContent = await _logsRepository.GetLogContent(logfileName);
            return Ok(logContent);
        }
        catch (HttpRequestException ex)
        {
            int statusCode = ex.StatusCode is null ? 500 : (int)ex.StatusCode;
            return StatusCode(statusCode, ex.Message);
        }
    }

    [HttpGet("download/{logfileName}")]
    [BearerHasPermissionFilter(Permission = EPermission.Logs)]
    public async Task<IActionResult> DownloadLog([FromRoute] string logfileName)
    {
        try
        {
            var content = await _logsRepository.DownloadLog(logfileName);
            return File(content, MediaTypeNames.Text.Plain, logfileName);
        }
        catch (HttpRequestException ex)
        {
            int statusCode = ex.StatusCode is null ? 500 : (int)ex.StatusCode;
            return StatusCode(statusCode, ex.Message);
        }
    }
}
