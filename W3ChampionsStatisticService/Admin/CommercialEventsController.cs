using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using W3C.Contracts.Admin.Permission;
using W3C.Domain.MatchmakingService;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using W3ChampionsStatisticService.WebApi.ExceptionFilters;

namespace W3ChampionsStatisticService.Admin;

/// <summary>
/// Proxy for the matchmaking commercial-events admin API (matchmaking-service docs/commercial-events.md).
/// Battle-tag route values are named targetBattleTag because BearerHasPermissionFilter overwrites the action argument
/// "battleTag" with the acting admin's battle tag, which is forwarded as actingBattleTag. Matchmaking errors are
/// answered by MatchmakingPassthroughExceptionFilter.
/// </summary>
[ApiController]
[Route("api/admin/commercial-events")]
[Trace]
[MatchmakingPassthroughExceptionFilter]
public class CommercialEventsController(MatchmakingServiceClient matchmakingServiceClient) : ControllerBase
{
    private readonly MatchmakingServiceClient _matchmakingServiceClient = matchmakingServiceClient;

    [HttpGet("allocations")]
    [BearerHasPermissionFilter(Permission = EPermission.CommercialLicense)]
    public async Task<IActionResult> GetAllocations() =>
        Ok(await _matchmakingServiceClient.GetCommercialEventAllocations());

    [HttpPost("allocations")]
    [BearerHasPermissionFilter(Permission = EPermission.CommercialLicense)]
    public async Task<IActionResult> CreateAllocation([FromBody] CommercialEventAllocationRequest body, [NoTrace] string battleTag) =>
        StatusCode(StatusCodes.Status201Created, await _matchmakingServiceClient.CreateCommercialEventAllocation(body, battleTag));

    [HttpPut("allocations/{allocationId}")]
    [BearerHasPermissionFilter(Permission = EPermission.CommercialLicense)]
    public async Task<IActionResult> UpdateAllocation([FromRoute] string allocationId, [FromBody] CommercialEventAllocationRequest body, [NoTrace] string battleTag)
    {
        if (InvalidSegment("allocationId", allocationId) is { } invalid) return invalid;
        return Ok(await _matchmakingServiceClient.UpdateCommercialEventAllocation(allocationId, body, battleTag));
    }

    [HttpPut("allocations/{allocationId}/members/{targetBattleTag}")]
    [BearerHasPermissionFilter(Permission = EPermission.CommercialLicense)]
    public async Task<IActionResult> AddAllocationMember([FromRoute] string allocationId, [FromRoute] string targetBattleTag, [NoTrace] string battleTag)
    {
        if ((InvalidSegment("allocationId", allocationId) ?? InvalidSegment("battleTag", targetBattleTag)) is { } invalid) return invalid;
        return Ok(await _matchmakingServiceClient.AddCommercialEventAllocationMember(allocationId, targetBattleTag, battleTag));
    }

    [HttpDelete("allocations/{allocationId}/members/{targetBattleTag}")]
    [BearerHasPermissionFilter(Permission = EPermission.CommercialLicense)]
    public async Task<IActionResult> RemoveAllocationMember([FromRoute] string allocationId, [FromRoute] string targetBattleTag, [NoTrace] string battleTag)
    {
        if ((InvalidSegment("allocationId", allocationId) ?? InvalidSegment("battleTag", targetBattleTag)) is { } invalid) return invalid;
        return Ok(await _matchmakingServiceClient.RemoveCommercialEventAllocationMember(allocationId, targetBattleTag, battleTag));
    }

    [HttpPost("allocations/{allocationId}/end")]
    [BearerHasPermissionFilter(Permission = EPermission.CommercialLicense)]
    public async Task<IActionResult> EndAllocation([FromRoute] string allocationId, [NoTrace] string battleTag)
    {
        if (InvalidSegment("allocationId", allocationId) is { } invalid) return invalid;
        return Ok(await _matchmakingServiceClient.EndCommercialEventAllocation(allocationId, battleTag));
    }

    [HttpDelete("allocations/{allocationId}")]
    [BearerHasPermissionFilter(Permission = EPermission.CommercialLicense)]
    public async Task<IActionResult> DeleteAllocation([FromRoute] string allocationId, [NoTrace] string battleTag)
    {
        if (InvalidSegment("allocationId", allocationId) is { } invalid) return invalid;
        await _matchmakingServiceClient.DeleteCommercialEventAllocation(allocationId, battleTag);
        return NoContent();
    }

    [HttpGet("allocations/{allocationId}/periods")]
    [BearerHasPermissionFilter(Permission = EPermission.CommercialLicense)]
    public async Task<IActionResult> GetAllocationPeriods([FromRoute] string allocationId)
    {
        if (InvalidSegment("allocationId", allocationId) is { } invalid) return invalid;
        return Ok(await _matchmakingServiceClient.GetCommercialEventAllocationPeriods(allocationId));
    }

    // Dot segments collapse in System.Uri and would send the admin-secret request to a different matchmaking path.
    // The body uses matchmaking's INVALID_REQUEST shape, so the website handles it like any matchmaking error.
    private static BadRequestObjectResult InvalidSegment(string field, string value) =>
        !string.IsNullOrWhiteSpace(value) && value is not ("." or "..")
            ? null
            : new BadRequestObjectResult(new { error = $"{field}: invalid", code = "INVALID_REQUEST", field });
}
