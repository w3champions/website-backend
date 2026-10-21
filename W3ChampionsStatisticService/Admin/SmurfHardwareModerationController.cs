using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3C.Contracts.Admin.Permission;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.Admin;

/// <summary>Request body of the routes that record a moderator's reason. The author is never read from the client.</summary>
public class SmurfReasonBody
{
    public string Reason { get; set; }
}

/// <summary>
/// Proxy for the matchmaking hardware smurf moderation API. Matchmaking's status code and JSON body are forwarded
/// unchanged; the acting moderator becomes the author of every write.
/// </summary>
[ApiController]
[Route("api/admin/smurf-detection")]
[Trace]
public class SmurfHardwareModerationController(IAdminRepository adminRepository) : ControllerBase
{
    private const int MaxLimit = 200;
    // Mirrors matchmaking's BATTLE_TAG (smurf-detection api/hardware-admin.ts), so no account it accepts is blocked here.
    // \z instead of $: .NET's $ also matches before a trailing newline, JavaScript's does not.
    private static readonly Regex BattleTagPattern = new(@"^[^|#\s][^|#]*#\d+\z", RegexOptions.Compiled);
    private static readonly Regex Hash8Pattern = new("^[0-9a-f]{8}$", RegexOptions.Compiled);

    private readonly IAdminRepository _adminRepository = adminRepository;

    [HttpGet("hardware/links/{targetBattleTag}")]
    [BearerHasPermissionFilter(Permission = EPermission.SmurfCheckerQuery)]
    public async Task<IActionResult> GetLinks([FromRoute] string targetBattleTag) =>
        InvalidBattleTag("battleTag", targetBattleTag) ?? Forward(await _adminRepository.GetHardwareLinks(targetBattleTag));

    [HttpGet("hardware/items/{targetBattleTag}")]
    [BearerHasPermissionFilter(Permission = EPermission.SmurfCheckerQuery)]
    public async Task<IActionResult> GetItems([FromRoute] string targetBattleTag) =>
        InvalidBattleTag("battleTag", targetBattleTag) ?? Forward(await _adminRepository.GetHardwareItems(targetBattleTag));

    [HttpGet("hardware/items/{targetBattleTag}/{hash8}")]
    [BearerHasPermissionFilter(Permission = EPermission.SmurfCheckerQuery)]
    public async Task<IActionResult> GetItem([FromRoute] string targetBattleTag, [FromRoute] string hash8) =>
        InvalidBattleTag("battleTag", targetBattleTag) ?? InvalidHash8(hash8) ?? Forward(await _adminRepository.GetHardwareItem(targetBattleTag, hash8));

    [HttpGet("hardware/audit")]
    [BearerHasPermissionFilter(Permission = EPermission.SmurfCheckerQuery)]
    public async Task<IActionResult> GetAudit([FromQuery(Name = "battleTag")] string targetBattleTag, [FromQuery] string before, [FromQuery] int? limit)
    {
        var invalid = (string.IsNullOrEmpty(targetBattleTag) ? null : InvalidBattleTag("battleTag", targetBattleTag)) ?? InvalidLimit(limit);
        return invalid ?? Forward(await _adminRepository.GetHardwareAudit(targetBattleTag, before, limit));
    }

    [HttpPost("links/{battleTagA}/{battleTagB}/smurf")]
    [BearerHasPermissionFilter(Permission = EPermission.SmurfCheckerAdministration)]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> SetSmurf([FromRoute] string battleTagA, [FromRoute] string battleTagB, [FromBody] SmurfReasonBody body, string actingPlayer) =>
        InvalidPair(battleTagA, battleTagB) ?? InvalidReason(body) ?? Forward(await _adminRepository.SetSmurfVerdict(battleTagA, battleTagB, body.Reason.Trim(), actingPlayer));

    [HttpDelete("links/{battleTagA}/{battleTagB}/smurf")]
    [BearerHasPermissionFilter(Permission = EPermission.SmurfCheckerAdministration)]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> RevokeSmurf([FromRoute] string battleTagA, [FromRoute] string battleTagB, string actingPlayer) =>
        InvalidPair(battleTagA, battleTagB) ?? Forward(await _adminRepository.RevokeSmurfVerdict(battleTagA, battleTagB, actingPlayer));

    [HttpPost("links/{battleTagA}/{battleTagB}/not-smurf")]
    [BearerHasPermissionFilter(Permission = EPermission.SmurfCheckerAdministration)]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> SetNotSmurf([FromRoute] string battleTagA, [FromRoute] string battleTagB, [FromBody] SmurfReasonBody body, string actingPlayer) =>
        InvalidPair(battleTagA, battleTagB) ?? InvalidReason(body) ?? Forward(await _adminRepository.SetNotSmurfVerdict(battleTagA, battleTagB, body.Reason.Trim(), actingPlayer));

    [HttpDelete("links/{battleTagA}/{battleTagB}/not-smurf")]
    [BearerHasPermissionFilter(Permission = EPermission.SmurfCheckerAdministration)]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> RevokeNotSmurf([FromRoute] string battleTagA, [FromRoute] string battleTagB, string actingPlayer) =>
        InvalidPair(battleTagA, battleTagB) ?? Forward(await _adminRepository.RevokeNotSmurfVerdict(battleTagA, battleTagB, actingPlayer));

    [HttpPost("hardware/items/{targetBattleTag}/{hash8}/trust")]
    [BearerHasPermissionFilter(Permission = EPermission.SmurfCheckerAdministration)]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> TrustItem([FromRoute] string targetBattleTag, [FromRoute] string hash8, [FromBody] SmurfReasonBody body, string actingPlayer) =>
        InvalidItem(targetBattleTag, hash8) ?? InvalidReason(body) ?? Forward(await _adminRepository.TrustHardwareItem(targetBattleTag, hash8, body.Reason.Trim(), actingPlayer));

    [HttpDelete("hardware/items/{targetBattleTag}/{hash8}/trust")]
    [BearerHasPermissionFilter(Permission = EPermission.SmurfCheckerAdministration)]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> RevokeTrust([FromRoute] string targetBattleTag, [FromRoute] string hash8, string actingPlayer) =>
        InvalidItem(targetBattleTag, hash8) ?? Forward(await _adminRepository.RevokeHardwareItemTrust(targetBattleTag, hash8, actingPlayer));

    [HttpPost("hardware/items/{targetBattleTag}/{hash8}/ignore")]
    [BearerHasPermissionFilter(Permission = EPermission.SmurfCheckerAdministration)]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> IgnoreItem([FromRoute] string targetBattleTag, [FromRoute] string hash8, [FromBody] SmurfReasonBody body, string actingPlayer) =>
        InvalidItem(targetBattleTag, hash8) ?? InvalidReason(body) ?? Forward(await _adminRepository.IgnoreHardwareItem(targetBattleTag, hash8, body.Reason.Trim(), actingPlayer));

    [HttpDelete("hardware/items/{targetBattleTag}/{hash8}/ignore")]
    [BearerHasPermissionFilter(Permission = EPermission.SmurfCheckerAdministration)]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> RevokeIgnore([FromRoute] string targetBattleTag, [FromRoute] string hash8, string actingPlayer) =>
        InvalidItem(targetBattleTag, hash8) ?? Forward(await _adminRepository.RevokeHardwareItemIgnore(targetBattleTag, hash8, actingPlayer));

    private static IActionResult Forward(MatchmakingProxyResult result) =>
        new ContentResult { StatusCode = result.StatusCode, ContentType = "application/json", Content = result.Body };

    private static IActionResult Invalid(string field, string message) =>
        new BadRequestObjectResult(new { error = $"{field}: {message}", code = "INVALID_REQUEST", field });

    private static IActionResult InvalidBattleTag(string field, string value) =>
        value != null && BattleTagPattern.IsMatch(value) ? null : Invalid(field, "invalid battleTag");

    private static IActionResult InvalidHash8(string value) =>
        value != null && Hash8Pattern.IsMatch(value) ? null : Invalid("hash8", "must be 8 lowercase hex characters");

    private static IActionResult InvalidLimit(int? limit) =>
        limit is null or (>= 1 and <= MaxLimit) ? null : Invalid("limit", $"must be between 1 and {MaxLimit}");

    private static IActionResult InvalidReason(SmurfReasonBody body) =>
        string.IsNullOrWhiteSpace(body?.Reason) ? Invalid("reason", "required") : null;

    private static IActionResult InvalidItem(string battleTag, string hash8) =>
        InvalidBattleTag("battleTag", battleTag) ?? InvalidHash8(hash8);

    private static IActionResult InvalidPair(string battleTagA, string battleTagB) =>
        InvalidBattleTag("battleTagA", battleTagA) ?? InvalidBattleTag("battleTagB", battleTagB);
}
