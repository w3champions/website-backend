using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3C.Domain.ChatService;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using System.Net;
using W3C.Contracts.Admin.Moderation;
using W3C.Contracts.Admin.Permission;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Moderation;

[ApiController]
[Route("api/moderation")]
[Trace]
public class ModerationController(ChatServiceClient chatServiceRepository) : ControllerBase
{
    private readonly ChatServiceClient _chatServiceRepository = chatServiceRepository;

    [HttpGet("loungeMute")]
    [InjectAuthToken]
    [BearerHasPermissionFilter(Permission = EPermission.Moderation)]
    public async Task<IActionResult> GetLoungeMutes([NoTrace] string authToken)
    {
        var loungeMutes = await _chatServiceRepository.GetLoungeMutes(authToken);
        return Ok(loungeMutes);
    }

    [HttpPost("loungeMute")]
    [InjectAuthToken]
    [BearerHasPermissionFilter(Permission = EPermission.Moderation)]
    public async Task<IActionResult> PostLoungeMute([FromBody] LoungeMute loungeMute, [NoTrace] string authToken)
    {
        if (loungeMute.battleTag == "")
        {
            return BadRequest("BattleTag cannot be empty.");
        }

        if (loungeMute.endDate == "")
        {
            return BadRequest("Ban End Date must be set.");
        }

        var result = await _chatServiceRepository.PostLoungeMute(loungeMute, authToken);
        if (result.StatusCode == HttpStatusCode.Forbidden)
        {
            return StatusCode(403);
        }
        if (result.StatusCode == HttpStatusCode.BadRequest)
        {
            var reason = result.Content.ReadAsStringAsync().Result;
            return BadRequest(reason);
        }
        if (result.StatusCode == HttpStatusCode.OK)
        {
            return Ok();
        }
        return StatusCode(500);
    }

    [HttpDelete("loungeMute/{bTag}")]
    [InjectAuthToken]
    [BearerHasPermissionFilter(Permission = EPermission.Moderation)]
    public async Task<IActionResult> DeleteLoungeMute([FromRoute] string bTag, [NoTrace] string authToken)
    {
        var result = await _chatServiceRepository.DeleteLoungeMute(bTag, authToken);
        if (result.StatusCode == HttpStatusCode.BadRequest)
        {
            var reason = result.Content.ReadAsStringAsync().Result;
            return BadRequest(reason);
        }
        if (result.StatusCode == HttpStatusCode.Forbidden)
        {
            return StatusCode(403);
        }
        if (result.StatusCode == HttpStatusCode.NotFound)
        {
            var reason = result.Content.ReadAsStringAsync().Result;
            return NotFound(reason);
        }
        if (result.StatusCode == HttpStatusCode.OK)
        {
            return Ok();
        }
        return StatusCode(500);
    }
}
