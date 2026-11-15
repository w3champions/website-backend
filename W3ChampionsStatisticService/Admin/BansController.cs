using System;
using System.Linq;
using System.Text.Json;
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
    public async Task<IActionResult> Create([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body) =>
        InvalidBody(body, out var json) ?? Forward(await _adminRepository.CreateBan(json, Caller()?.BattleTag, CallerHasMachineAccess()));

    [HttpPatch("{id}")]
    [BearerHasPermissionFilter(Permission = EPermission.Moderation)]
    [InjectActingPlayerAuthCode]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    public async Task<IActionResult> Edit([FromRoute] string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var banId = RouteValue(id);
        return InvalidSegment(banId, nameof(id)) ?? InvalidBody(body, out var json)
            ?? Forward(await _adminRepository.EditBan(banId, json, Caller()?.BattleTag, CallerHasMachineAccess()));
    }

    [HttpPost("{id}/revoke")]
    [BearerHasPermissionFilter(Permission = EPermission.Moderation)]
    [InjectActingPlayerAuthCode]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    public async Task<IActionResult> Revoke([FromRoute] string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var banId = RouteValue(id);
        return InvalidSegment(banId, nameof(id)) ?? InvalidBody(body, out var json)
            ?? Forward(await _adminRepository.RevokeBan(banId, json, Caller()?.BattleTag, CallerHasMachineAccess()));
    }

    [HttpGet("accounts/{accountKey}/machine-ids")]
    [BearerHasPermissionFilter(Permission = EPermission.Moderation)]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> MachineIds([FromRoute] string accountKey)
    {
        var key = RouteValue(accountKey);
        return MachineRefusal("accounts/{accountKey}/machine-ids") ?? InvalidSegment(key, nameof(accountKey))
            ?? Forward(await _adminRepository.GetBanAccountMachineIds(key, CallerHasMachineAccess()));
    }

    [HttpGet("machine-ids/{machineId}/accounts")]
    [BearerHasPermissionFilter(Permission = EPermission.Moderation)]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> MachineIdAccounts([FromRoute] string machineId, [FromQuery] string excludeAccount)
    {
        var id = RouteValue(machineId);
        return MachineRefusal("machine-ids/{machineId}/accounts") ?? InvalidSegment(id, nameof(machineId))
            ?? Forward(await _adminRepository.GetBanMachineIdAccounts(id, excludeAccount, CallerHasMachineAccess()));
    }

    [HttpPost("machine-impact")]
    [BearerHasPermissionFilter(Permission = EPermission.Moderation)]
    [InjectActingPlayerAuthCode]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    public async Task<IActionResult> MachineImpact([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body) =>
        MachineRefusal("machine-impact") ?? InvalidBody(body, out var json) ?? Forward(await _adminRepository.GetBanMachineImpact(json, CallerHasMachineAccess()));

    [HttpPost("status")]
    [BearerHasPermissionFilter(Permission = EPermission.Moderation)]
    [InjectActingPlayerAuthCode]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    public async Task<IActionResult> Status([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body) =>
        InvalidBody(body, out var json) ?? Forward(await _adminRepository.GetBanStatuses(json, CallerHasMachineAccess()));

    private W3CUserAuthenticationDto Caller() => InjectActingPlayerAuthCodeAttribute.GetActingPlayerUser(HttpContext);

    private bool CallerHasMachineAccess() => CallerMachineAccess.Of(HttpContext);

    // ASP.NET Core decodes a route value except for %2F, which it leaves encoded; the repository encodes once, so decode
    // once here. A value is therefore unescaped twice in total (Kestrel, then here): "%252F" arrives as "%2F" and becomes
    // "/". That is safe: the result is re-encoded as one path segment (Segment), InvalidSegment checks the decoded value
    // for dot segments, and no ban id or account key contains a literal "%", so no real value changes meaning.
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

    // Bound as a JsonElement, not a JsonObject: binding a JsonObject throws an ArgumentException (a 500) on a duplicate
    // top-level key, which the input formatter does not turn into a model error. The object keeps the case-insensitive
    // property names of the web JSON options, so "actor", "Actor" and "ACTOR" are one key for ForwardedBody, and
    // duplicates such as "Actor" with "ACTOR" are refused here with a 400 instead.
    private static readonly JsonNodeOptions BodyOptions = new() { PropertyNameCaseInsensitive = true };

    private static IActionResult InvalidBody(JsonElement? raw, out JsonObject body)
    {
        body = null;
        if (raw is not { } element || element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return Error(StatusCodes.Status400BadRequest, "body: required", "VALIDATION");
        if (element.ValueKind != JsonValueKind.Object)
            return Error(StatusCodes.Status400BadRequest, "body: must be a JSON object", "VALIDATION");
        var candidate = JsonObject.Create(element, BodyOptions)!;
        if (HasDuplicateKeys(candidate))
            return Error(StatusCodes.Status400BadRequest, "body: duplicate property", "VALIDATION");
        body = candidate;
        return null;
    }

    // JsonObject builds its property dictionaries lazily; walking every object materializes each one here, so a
    // duplicate key at any depth is a 400 now instead of an exception when the body is copied for matchmaking.
    private static bool HasDuplicateKeys(JsonNode node)
    {
        try
        {
            return node switch
            {
                JsonObject obj => obj.Any(property => HasDuplicateKeys(property.Value)),
                JsonArray array => array.Any(HasDuplicateKeys),
                _ => false,
            };
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static IActionResult Error(int status, string msg, string code) =>
        new ObjectResult(new { errors = new[] { new { msg, code } } }) { StatusCode = status };
}
