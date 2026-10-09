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
    public async Task<IActionResult> PutTaggedPlayer([FromRoute] string targetBattleTag, [FromBody] CommercialLicenseTaggedPlayerBody body, [NoTrace] string battleTag)
    {
        if (!IsValidTarget(targetBattleTag))
            return BadRequest(new { error = "invalid_battletag" });

        if (body?.Notify == null)
            return BadRequest(new { error = "invalid_request" });

        // Mirrors matchmaking's validation order and texts. Missing flags must not default to false,
        // because forwarding false would clear stored restrictions.
        // A non-object restrictions value or a non-boolean flag (e.g. "true") fails model binding before this
        // action runs, so ASP.NET answers with a ValidationProblemDetails 400 rather than this {error} shape.
        var restrictions = body.Restrictions;
        if (restrictions != null)
        {
            if (restrictions.AsPlayer == null)
                return BadRequest(new { error = "restrictions.asPlayer must be a boolean" });
            if (restrictions.AsObserver == null)
                return BadRequest(new { error = "restrictions.asObserver must be a boolean" });
            if (restrictions.FloTv is not ("none" or "custom" or "all"))
                return BadRequest(new { error = "restrictions.floTv must be one of none, custom, all" });
        }

        var request = new CommercialLicenseTaggedPlayerRequest
        {
            note = body.Note ?? "",
            notify = body.Notify.Value,
            actingBattleTag = battleTag,
            Restrictions = restrictions == null ? null : new CommercialLicenseRestrictions
            {
                AsPlayer = restrictions.AsPlayer.Value,
                AsObserver = restrictions.AsObserver.Value,
                FloTv = restrictions.FloTv,
            },
        };
        return Ok(await _matchmakingServiceClient.UpsertCommercialLicenseTaggedPlayer(targetBattleTag, request));
    }

    [HttpDelete("tagged-players/{targetBattleTag}")]
    [BearerHasPermissionFilter(Permission = EPermission.CommercialLicense)]
    public async Task<IActionResult> DeleteTaggedPlayer([FromRoute] string targetBattleTag)
    {
        if (!IsValidTarget(targetBattleTag))
            return BadRequest(new { error = "invalid_battletag" });

        await _matchmakingServiceClient.DeleteCommercialLicenseTaggedPlayer(targetBattleTag);
        return NoContent();
    }

    // Dot segments collapse in System.Uri and would send the admin-secret request to a different matchmaking path.
    private static bool IsValidTarget(string targetBattleTag) =>
        !string.IsNullOrWhiteSpace(targetBattleTag) && targetBattleTag is not ("." or "..");
}

/// <summary>Inbound PUT body: deliberately has no acting battleTag, which always comes from the bearer token.</summary>
public class CommercialLicenseTaggedPlayerBody
{
    public string Note { get; set; }
    public bool? Notify { get; set; }

    /// <summary>Optional. When absent it is not forwarded and matchmaking keeps the stored restrictions.</summary>
    public CommercialLicenseRestrictionsBody Restrictions { get; set; }
}

/// <summary>Inbound restrictions: nullable flags so a missing value is detectable instead of binding to false.</summary>
public class CommercialLicenseRestrictionsBody
{
    public bool? AsPlayer { get; set; }
    public bool? AsObserver { get; set; }
    public string FloTv { get; set; }
}
