using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Serilog;
using W3C.Contracts.Admin.Permission;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using static W3ChampionsStatisticService.Admin.SmurfModerationResults;

namespace W3ChampionsStatisticService.Admin;

/// <summary>
/// Proxy for the matchmaking ban admin API. Matchmaking's status code and JSON body are forwarded unchanged; matchmaking
/// owns the rules. The actor and the machine-ban access are derived from the caller's token, never from the request, so
/// no action has a parameter that could bind a client value to either.
/// </summary>
[ApiController]
[Route("api/admin/bans")]
[Trace]
public class BansController(IAdminRepository adminRepository) : ControllerBase
{
    // Transport bound for the JSON bodies; not a rule about their content, which lives in matchmaking.
    internal const int MaxJsonBodyBytes = 64 * 1024;

    private readonly IAdminRepository _adminRepository = adminRepository;

    [HttpGet("")]
    [BearerHasPermissionFilter(Permission = EPermission.Moderation)]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> List([FromQuery] string status, [FromQuery] string author, [FromQuery] string account,
        [FromQuery] string machineOnly, [FromQuery] string sort, [FromQuery] string dir, [FromQuery] string page, [FromQuery] string pageSize) =>
        Forward(await _adminRepository.ListBans(new BanListFilter(status, author, account, machineOnly, sort, dir, page, pageSize), CallerHasMachineAccess()));

    [HttpGet("authors")]
    [BearerHasPermissionFilter(Permission = EPermission.Moderation)]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> Authors() =>
        Forward(await _adminRepository.GetBanAuthors(CallerHasMachineAccess()));

    [HttpGet("account-lookup")]
    [BearerHasPermissionFilter(Permission = EPermission.Moderation)]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> Lookup([FromQuery] string q) =>
        Forward(await _adminRepository.LookupBanAccounts(q, CallerHasMachineAccess()));

    [HttpGet("accounts/{accountKey}")]
    [BearerHasPermissionFilter(Permission = EPermission.Moderation)]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> Account([FromRoute] string accountKey)
    {
        var key = RouteValue(accountKey);
        return InvalidSegment(key, nameof(accountKey)) ?? Forward(await _adminRepository.GetBanAccount(key, CallerHasMachineAccess()));
    }

    [HttpPost("")]
    [BearerHasPermissionFilter(Permission = EPermission.Moderation)]
    [InjectActingPlayerAuthCode]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    public async Task<IActionResult> Create([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonObject body) =>
        MissingBody(body) ?? Forward(await _adminRepository.CreateBan(body, Caller()?.BattleTag, CallerHasMachineAccess()));

    [HttpPatch("{id}")]
    [BearerHasPermissionFilter(Permission = EPermission.Moderation)]
    [InjectActingPlayerAuthCode]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    public async Task<IActionResult> Edit([FromRoute] string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonObject body)
    {
        var banId = RouteValue(id);
        return InvalidSegment(banId, nameof(id)) ?? MissingBody(body)
            ?? Forward(await _adminRepository.EditBan(banId, body, Caller()?.BattleTag, CallerHasMachineAccess()));
    }

    [HttpPost("{id}/revoke")]
    [BearerHasPermissionFilter(Permission = EPermission.Moderation)]
    [InjectActingPlayerAuthCode]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    public async Task<IActionResult> Revoke([FromRoute] string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonObject body)
    {
        var banId = RouteValue(id);
        return InvalidSegment(banId, nameof(id)) ?? MissingBody(body)
            ?? Forward(await _adminRepository.RevokeBan(banId, body, Caller()?.BattleTag, CallerHasMachineAccess()));
    }

    [HttpGet("accounts/{accountKey}/machine-ids")]
    [BearerHasPermissionFilter(Permission = EPermission.Moderation)]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> MachineIds([FromRoute] string accountKey)
    {
        var key = RouteValue(accountKey);
        return MachineRefusal("accounts/{accountKey}/machine-ids") ?? InvalidSegment(key, nameof(accountKey))
            ?? Forward(await _adminRepository.GetBanAccountMachineIds(key));
    }

    [HttpGet("machine-ids/{machineId}/accounts")]
    [BearerHasPermissionFilter(Permission = EPermission.Moderation)]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> MachineIdAccounts([FromRoute] string machineId, [FromQuery] string excludeAccount)
    {
        var id = RouteValue(machineId);
        return MachineRefusal("machine-ids/{machineId}/accounts") ?? InvalidSegment(id, nameof(machineId))
            ?? Forward(await _adminRepository.GetBanMachineIdAccounts(id, excludeAccount));
    }

    [HttpPost("machine-impact")]
    [BearerHasPermissionFilter(Permission = EPermission.Moderation)]
    [InjectActingPlayerAuthCode]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    public async Task<IActionResult> MachineImpact([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonObject body) =>
        MachineRefusal("machine-impact") ?? MissingBody(body) ?? Forward(await _adminRepository.GetBanMachineImpact(body));

    [HttpPost("status")]
    [BearerHasPermissionFilter(Permission = EPermission.Moderation)]
    [InjectActingPlayerAuthCode]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    public async Task<IActionResult> Status([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonObject body) =>
        MissingBody(body) ?? Forward(await _adminRepository.GetBanStatuses(body, CallerHasMachineAccess()));

    private W3CUserAuthenticationDto Caller() => InjectActingPlayerAuthCodeAttribute.GetActingPlayerUser(HttpContext);

    private bool CallerHasMachineAccess() => Caller()?.Permissions?.Contains(EPermission.AdminModeration) == true;

    // ASP.NET Core leaves %2F encoded inside a route value; the repository encodes once, so decode once here.
    private static string RouteValue(string raw) => raw == null ? null : Uri.UnescapeDataString(raw);

    private IActionResult MachineRefusal(string route)
    {
        if (CallerHasMachineAccess()) return null;
        Log.Warning("Machine ban route {Route} refused for {BattleTag}: AdminModeration missing", route, Caller()?.BattleTag);
        return Error(StatusCodes.Status403Forbidden, "Permission denied, please contact an Administrator", "MACHINE_BAN_PERMISSION");
    }

    // A dot segment would make the outgoing URL resolve to a different matchmaking admin path.
    private static IActionResult InvalidSegment(string value, string field) =>
        string.IsNullOrEmpty(value) || value is "." or ".." ? Error(StatusCodes.Status400BadRequest, $"{field}: invalid", "VALIDATION") : null;

    private static IActionResult MissingBody(JsonObject body) =>
        body == null ? Error(StatusCodes.Status400BadRequest, "body: required", "VALIDATION") : null;

    private static IActionResult Error(int status, string msg, string code) =>
        new ObjectResult(new { errors = new[] { new { msg, code } } }) { StatusCode = status };
}
