using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3C.Contracts.Admin.Permission;
using W3C.Domain.MatchmakingService;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.Admin;

[ApiController]
[Route("api/admin/commercial-license")]
[Trace]
public class CommercialLicenseController(MatchmakingServiceClient matchmakingServiceClient) : ControllerBase
{
    private readonly MatchmakingServiceClient _matchmakingServiceClient = matchmakingServiceClient;

    [HttpGet("tagged-players")]
    [BearerHasPermissionFilter(Permission = EPermission.CommercialLicense)]
    public async Task<IActionResult> GetTaggedPlayers()
    {
        return Ok(await _matchmakingServiceClient.GetCommercialLicenseTaggedPlayers());
    }

    // The route value is named targetBattleTag because BearerHasPermissionFilter overwrites the
    // action argument "battleTag" with the acting admin's battleTag.
    [HttpPut("tagged-players/{targetBattleTag}")]
    [BearerHasPermissionFilter(Permission = EPermission.CommercialLicense)]
    public async Task<IActionResult> PutTaggedPlayer([FromRoute] string targetBattleTag, [FromBody] CommercialLicenseTaggedPlayerRequest request, [NoTrace] string battleTag)
    {
        if (request == null)
            return BadRequest(new { error = "invalid_request" });

        request.note ??= "";
        request.actingBattleTag = battleTag;
        return Ok(await _matchmakingServiceClient.UpsertCommercialLicenseTaggedPlayer(targetBattleTag, request));
    }

    [HttpDelete("tagged-players/{targetBattleTag}")]
    [BearerHasPermissionFilter(Permission = EPermission.CommercialLicense)]
    public async Task<IActionResult> DeleteTaggedPlayer([FromRoute] string targetBattleTag)
    {
        await _matchmakingServiceClient.DeleteCommercialLicenseTaggedPlayer(targetBattleTag);
        return NoContent();
    }
}
