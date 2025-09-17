using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using W3C.Contracts.Admin.Permission;
using W3C.Domain.UpdateService;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Maps;

[ApiController]
[Route("api/replays")]
[Trace]
public class ReplaysController(
    ReplayServiceClient replayServiceClient,
    IMatchRepository matchRepository) : ControllerBase
{
    private readonly ReplayServiceClient _replayServiceClient = replayServiceClient;
    private readonly IMatchRepository _matchRepository = matchRepository;

    [HttpGet("{gameId}")]
    [ReplayRateLimit(
        StrictHourlyLimit = 10,
        StrictDailyLimit = 50,
        RelaxedHourlyLimit = 30,
        RelaxedDailyLimit = 100,
        MatchAgeThresholdDays = 7)]
    public async Task<IActionResult> GetReplay(string gameId)
    {
        var floMatchId = await _matchRepository.GetFloIdFromId(gameId);
        if (floMatchId == 0)
        {
            return NotFound();
        }
        var replayStream = await _replayServiceClient.GenerateReplay(floMatchId);
        return File(replayStream, "application/octet-stream", $"{gameId}.w3g");
    }

    [HttpGet("{gameId}/chats")]
    [BearerHasPermissionFilter(Permission = EPermission.Moderation)]
    public async Task<IActionResult> GetReplayChatLogs(string gameId)
    {
        var floMatchId = await _matchRepository.GetFloIdFromId(gameId);
        if (floMatchId == 0)
        {
            return NotFound();
        }
        var data = await _replayServiceClient.GetChatLogs(floMatchId);
        return Ok(data);
    }

    [HttpGet("by-flo-id/{floMatchId}")]
    [ReplayRateLimit(
        StrictHourlyLimit = 10,
        StrictDailyLimit = 50,
        RelaxedHourlyLimit = 30,
        RelaxedDailyLimit = 100,
        MatchAgeThresholdDays = 7)]
    public async Task<IActionResult> GetReplay(int floMatchId)
    {
        if (floMatchId == 0)
        {
            return NotFound();
        }
        var replayStream = await _replayServiceClient.GenerateReplay(floMatchId);
        return File(replayStream, "application/octet-stream", $"{floMatchId}.w3g");
    }

    [HttpGet("by-flo-id/{floMatchId}/chats")]
    [BearerHasPermissionFilter(Permission = EPermission.Moderation)]
    public async Task<IActionResult> GetReplayChatLogs(int floMatchId)
    {
        if (floMatchId == 0)
        {
            return NotFound();
        }
        var data = await _replayServiceClient.GetChatLogs(floMatchId);
        return Ok(data);
    }
}
