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

    /// <param name="gameMode">Omit to list every mode.</param>
    /// <param name="cursor">From a previous response's nextCursor. Omit for the first page.</param>
    /// <param name="playerBattleTag">
    /// The player to filter matches by. Deliberately NOT named "battleTag": BearerHasPermissionFilter
    /// unconditionally overwrites an action argument named exactly "battleTag" with the acting admin's
    /// own tag (see BearerHasPermissionFilter.cs:33), which is the intended behaviour for endpoints that
    /// use "battleTag" purely to capture who performed the action. Here it is a caller-supplied search
    /// filter, so it must use a different name or the filter would silently clobber it. Do not rename
    /// this back to "battleTag".
    /// </param>
    [HttpGet("canceled")]
    [BearerHasPermissionFilter(Permission = EPermission.Moderation)]
    public async Task<IActionResult> GetCanceledMatches(
        [FromQuery] GameMode? gameMode = null,
        [FromQuery] string playerBattleTag = null,
        [FromQuery] string cursor = null,
        [FromQuery] int itemsPerPage = 25)
    {
        if (itemsPerPage < 1) itemsPerPage = 25;
        if (itemsPerPage > MaxPageSize) itemsPerPage = MaxPageSize;

        var result = await _matchmakingServiceClient.GetCanceledMatches(new CanceledMatchesGetRequest
        {
            ItemsPerPage = itemsPerPage,
            Cursor = cursor,
            GameMode = gameMode,
            BattleTag = playerBattleTag,
        });

        if (result == null)
        {
            // Not a 404: the collection exists and the request was well-formed, we just
            // could not reach the only service that knows the answer. A 404 would tell
            // the client there are no cancelled matches, which is a different -- and
            // wrong -- statement, and one a moderator might act on. Genuinely bad
            // requests (unknown game mode, stale cursor) are rejected upstream with a
            // 400 and surface as a 400, not here.
            return StatusCode(502, "The matchmaking service did not return cancelled matches.");
        }

        return Ok(result);
    }
}
