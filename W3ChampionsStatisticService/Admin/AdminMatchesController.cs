using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3C.Contracts.Admin.Permission;
using W3C.Contracts.Matchmaking;
using W3C.Domain.MatchmakingService;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.Admin;

/// <summary>
/// Moderation-facing views over matches that never produced a ranked result.
///
/// Cancelled matches are read straight from matchmaking, which is the only
/// complete record: the MatchCanceledEvent stream omits every game-creation
/// failure, so a read model built from it would be missing exactly the matches
/// moderators care about.
/// </summary>
[ApiController]
[Route("api/admin/matches")]
[Trace]
public class AdminMatchesController(MatchmakingServiceClient matchmakingServiceClient) : ControllerBase
{
    private const int MaxPageSize = 100;

    private readonly MatchmakingServiceClient _matchmakingServiceClient = matchmakingServiceClient;

    /// <param name="gameMode">GameMode.Undefined (0) lists every mode.</param>
    [HttpGet("canceled")]
    [BearerHasPermissionFilter(Permission = EPermission.Moderation)]
    public async Task<IActionResult> GetCanceledMatches(
        [FromQuery] GameMode gameMode = GameMode.Undefined,
        [FromQuery] string battleTag = null,
        [FromQuery] int page = 1,
        [FromQuery] int itemsPerPage = 25)
    {
        if (page < 1) page = 1;
        if (itemsPerPage < 1) itemsPerPage = 25;
        if (itemsPerPage > MaxPageSize) itemsPerPage = MaxPageSize;

        var result = await _matchmakingServiceClient.GetCanceledMatches(new CanceledMatchesGetRequest
        {
            Page = page,
            ItemsPerPage = itemsPerPage,
            GameMode = gameMode,
            BattleTag = battleTag,
        });

        if (result == null)
        {
            return StatusCode(502, "The matchmaking service did not return cancelled matches.");
        }

        return Ok(result);
    }
}
