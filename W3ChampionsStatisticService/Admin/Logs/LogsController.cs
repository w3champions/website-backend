using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using System.Net.Http;
using System.Net.Mime;

namespace W3ChampionsStatisticService.Admin.Logs;

[ApiController]
[Route("api/admin/logs")]
public class LogsController : ControllerBase
{
    private readonly ILogsRepository _logsRepository;

    public LogsController(
        ILogsRepository logsRepository)
    {
        _logsRepository = logsRepository;
    }

    [HttpGet]
    [HasLogsPermission]
    public async Task<IActionResult> GetLogfileNames()
    {
        try {
            var logfileNames = await _logsRepository.GetLogfileNames();
            return Ok(logfileNames);
        } catch (HttpRequestException ex) {
            return StatusCode((int)ex.StatusCode, ex.Message);
        }
    }

    [HttpGet("{logfileName}")]
    [HasLogsPermission]
    public async Task<IActionResult> GetLogContent([FromRoute] string logfileName)
    {
        try {
            var logContent = await _logsRepository.GetLogContent(logfileName);
            return Ok(logContent);
        } catch (HttpRequestException ex) {
            return StatusCode((int)ex.StatusCode, ex.Message);
        }
    }

    [HttpGet("download/{logfileName}")]
    [HasLogsPermission]
    public async Task<IActionResult> DownloadLog([FromRoute] string logfileName)
    {
        try {
            var content = await _logsRepository.DownloadLog(logfileName);
            return File(content, MediaTypeNames.Text.Plain, logfileName);
        } catch (HttpRequestException ex) {
            return StatusCode((int)ex.StatusCode, ex.Message);
        }
    }
}
