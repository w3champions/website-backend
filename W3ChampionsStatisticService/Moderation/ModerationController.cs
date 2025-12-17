using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3C.Domain.ChatService;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using W3C.Contracts.Admin.Moderation;
using W3C.Contracts.Admin.Permission;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Moderation;

[ApiController]
[Route("api/moderation")]
[Trace]
public class ModerationController(ChatServiceClient chatServiceClient) : ControllerBase
{
    private readonly ChatServiceClient _chatServiceClient = chatServiceClient;

    [HttpGet("lounge-mute")]
    [InjectAuthToken]
    [BearerHasPermissionFilter(Permission = EPermission.Moderation)]
    public async Task<IActionResult> GetLoungeMutes([NoTrace] string authToken)
    {
        LoungeMuteResponse[] loungeMutes = await _chatServiceClient.GetLoungeMutes(authToken);
        return Ok(loungeMutes);
    }

    [HttpPost("lounge-mute/batch")]
    [InjectAuthToken]
    [BearerHasPermissionFilter(Permission = EPermission.Moderation)]
    public async Task<IActionResult> GetLoungeMutesBatch([FromBody] BattleTagsBatchRequest request, [NoTrace] string authToken)
    {
        if (request?.BattleTags == null || !request.BattleTags.Any())
        {
            return BadRequest("BattleTags list cannot be empty");
        }

        if (request.BattleTags.Count > 100)
        {
            return BadRequest("Maximum 100 battleTags per request");
        }

        // Fetch all lounge mutes
        var allLoungeMutes = await _chatServiceClient.GetLoungeMutes(authToken);

        if (allLoungeMutes == null)
        {
            return Ok(System.Array.Empty<LoungeMuteResponse>());
        }

        // Filter to exact matches only (case-insensitive) and select most recent mute per battleTag
        var filteredMutes = allLoungeMutes
            .Where(m => request.BattleTags.Any(tag =>
                string.Equals(m.battleTag, tag, StringComparison.OrdinalIgnoreCase)))
            .GroupBy(m => m.battleTag.ToLower())
            .Select(g => g.OrderByDescending(m => m.insertDate).First())
            .ToList();

        return Ok(filteredMutes);
    }

    [HttpPost("lounge-mute")]
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

        string response = await _chatServiceClient.PostLoungeMute(loungeMute, authToken);

        return Ok(response);
    }

    [HttpDelete("lounge-mute/{bTag}")]
    [InjectAuthToken]
    [BearerHasPermissionFilter(Permission = EPermission.Moderation)]
    public async Task<IActionResult> DeleteLoungeMute([FromRoute] string bTag, [NoTrace] string authToken)
    {
        string response = await _chatServiceClient.DeleteLoungeMute(bTag, authToken);
        return Ok(response);
    }

    [HttpGet("launcher-chat/{chatRoom}")]
    [NoTrace]
    [InjectAuthToken]
    [BearerHasPermissionFilter(Permission = EPermission.Moderation)]
    public async Task<IActionResult> GetChatRoomMessages([FromRoute] string chatRoom, string authToken)
    {
        ChatMessage[] chatHistory = await _chatServiceClient.GetChatRoomMessages(chatRoom, authToken);
        return Ok(chatHistory);
    }
}
