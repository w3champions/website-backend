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

    [HttpGet("events")]
    [BearerHasPermissionFilter(Permission = EPermission.CommercialLicense)]
    public async Task<IActionResult> GetEvents([FromQuery] string status, [FromQuery] string phase, [FromQuery] string allocationId, [FromQuery] string q) =>
        Ok(await _matchmakingServiceClient.GetCommercialEvents(status, phase, allocationId, q));

    [HttpGet("events/{eventId}")]
    [BearerHasPermissionFilter(Permission = EPermission.CommercialLicense)]
    public async Task<IActionResult> GetEvent([FromRoute] string eventId)
    {
        if (InvalidSegment("eventId", eventId) is { } invalid) return invalid;
        return Ok(await _matchmakingServiceClient.GetCommercialEvent(eventId));
    }

    [HttpPost("events")]
    [BearerHasPermissionFilter(Permission = EPermission.CommercialLicense)]
    public async Task<IActionResult> CreateEvent([FromBody] CommercialEventCreateRequest body, [NoTrace] string battleTag) =>
        StatusCode(StatusCodes.Status201Created, await _matchmakingServiceClient.CreateCommercialEvent(body, battleTag));

    [HttpPut("events/{eventId}")]
    [BearerHasPermissionFilter(Permission = EPermission.CommercialLicense)]
    public async Task<IActionResult> UpdateEvent([FromRoute] string eventId, [FromBody] CommercialEventUpdateRequest body, [NoTrace] string battleTag)
    {
        if (InvalidSegment("eventId", eventId) is { } invalid) return invalid;
        return Ok(await _matchmakingServiceClient.UpdateCommercialEvent(eventId, body, battleTag));
    }

    [HttpPost("events/{eventId}/move")]
    [BearerHasPermissionFilter(Permission = EPermission.CommercialLicense)]
    public async Task<IActionResult> MoveEvent([FromRoute] string eventId, [FromBody] CommercialEventMoveRequest body, [NoTrace] string battleTag)
    {
        if (InvalidSegment("eventId", eventId) is { } invalid) return invalid;
        return Ok(await _matchmakingServiceClient.MoveCommercialEvent(eventId, body, battleTag));
    }

    [HttpPost("events/{eventId}/close")]
    [BearerHasPermissionFilter(Permission = EPermission.CommercialLicense)]
    public async Task<IActionResult> CloseEvent([FromRoute] string eventId, [NoTrace] string battleTag)
    {
        if (InvalidSegment("eventId", eventId) is { } invalid) return invalid;
        return Ok(await _matchmakingServiceClient.CloseCommercialEvent(eventId, battleTag));
    }

    [HttpPost("events/{eventId}/suspend")]
    [BearerHasPermissionFilter(Permission = EPermission.CommercialLicense)]
    public async Task<IActionResult> SuspendEvent([FromRoute] string eventId, [FromBody] CommercialEventSuspendRequest body, [NoTrace] string battleTag)
    {
        if (InvalidSegment("eventId", eventId) is { } invalid) return invalid;
        return Ok(await _matchmakingServiceClient.SuspendCommercialEvent(eventId, body, battleTag));
    }

    [HttpPost("events/{eventId}/unsuspend")]
    [BearerHasPermissionFilter(Permission = EPermission.CommercialLicense)]
    public async Task<IActionResult> UnsuspendEvent([FromRoute] string eventId, [NoTrace] string battleTag)
    {
        if (InvalidSegment("eventId", eventId) is { } invalid) return invalid;
        return Ok(await _matchmakingServiceClient.UnsuspendCommercialEvent(eventId, battleTag));
    }

    [HttpGet("events/{eventId}/people")]
    [BearerHasPermissionFilter(Permission = EPermission.CommercialLicense)]
    public async Task<IActionResult> GetEventPeople([FromRoute] string eventId)
    {
        if (InvalidSegment("eventId", eventId) is { } invalid) return invalid;
        return Ok(await _matchmakingServiceClient.GetCommercialEventPeople(eventId));
    }

    [HttpPut("events/{eventId}/people/{targetBattleTag}")]
    [BearerHasPermissionFilter(Permission = EPermission.CommercialLicense)]
    public async Task<IActionResult> AddEventPerson([FromRoute] string eventId, [FromRoute] string targetBattleTag, [FromBody] CommercialEventPersonRequest body, [NoTrace] string battleTag)
    {
        if ((InvalidSegment("eventId", eventId) ?? InvalidSegment("battleTag", targetBattleTag)) is { } invalid) return invalid;
        return Ok(await _matchmakingServiceClient.AddCommercialEventPerson(eventId, targetBattleTag, body, battleTag));
    }

    [HttpDelete("events/{eventId}/people/{targetBattleTag}")]
    [BearerHasPermissionFilter(Permission = EPermission.CommercialLicense)]
    public async Task<IActionResult> RemoveEventPerson([FromRoute] string eventId, [FromRoute] string targetBattleTag, [NoTrace] string battleTag)
    {
        if ((InvalidSegment("eventId", eventId) ?? InvalidSegment("battleTag", targetBattleTag)) is { } invalid) return invalid;
        return Ok(await _matchmakingServiceClient.RemoveCommercialEventPerson(eventId, targetBattleTag, battleTag));
    }

    [HttpGet("events/{eventId}/games")]
    [BearerHasPermissionFilter(Permission = EPermission.CommercialLicense)]
    public async Task<IActionResult> GetEventGames([FromRoute] string eventId, [FromQuery] string cursor, [FromQuery] string limit)
    {
        if (InvalidSegment("eventId", eventId) is { } invalid) return invalid;
        return Ok(await _matchmakingServiceClient.GetCommercialEventGames(eventId, cursor, limit));
    }

    [HttpGet("games/active")]
    [BearerHasPermissionFilter(Permission = EPermission.CommercialLicense)]
    public async Task<IActionResult> GetActiveGames() =>
        Ok(await _matchmakingServiceClient.GetActiveCommercialEventGames());

    [HttpPost("games/{matchId}/terminate")]
    [BearerHasPermissionFilter(Permission = EPermission.CommercialLicense)]
    public async Task<IActionResult> TerminateGame([FromRoute] string matchId, [NoTrace] string battleTag)
    {
        if (InvalidSegment("matchId", matchId) is { } invalid) return invalid;
        await _matchmakingServiceClient.TerminateCommercialEventGame(matchId, battleTag);
        return NoContent();
    }

    [HttpGet("audit")]
    [BearerHasPermissionFilter(Permission = EPermission.CommercialLicense)]
    public async Task<IActionResult> GetAudit([FromQuery] string eventId, [FromQuery] string allocationId) =>
        Ok(await _matchmakingServiceClient.GetCommercialEventAudit(eventId, allocationId));

    // A read with a JSON body (index P41): no acting admin is taken or forwarded. matchmaking validates battleTags.
    [HttpPost("roles/lookup")]
    [BearerHasPermissionFilter(Permission = EPermission.CommercialLicense)]
    public async Task<IActionResult> GetRoleHints([FromBody] CommercialEventRoleLookupRequest body) =>
        Ok(await _matchmakingServiceClient.GetCommercialEventRoleHints(body.BattleTags));

    // Dot segments collapse in System.Uri and would send the admin-secret request to a different matchmaking path.
    // The body uses matchmaking's INVALID_REQUEST shape, so the website handles it like any matchmaking error.
    private static BadRequestObjectResult InvalidSegment(string field, string value) =>
        !string.IsNullOrWhiteSpace(value) && value is not ("." or "..")
            ? null
            : new BadRequestObjectResult(new { error = $"{field}: invalid", code = "INVALID_REQUEST", field });
}
