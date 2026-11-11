using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using W3C.Contracts.Admin.Permission;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using static W3ChampionsStatisticService.Admin.SmurfModerationResults;

namespace W3ChampionsStatisticService.Admin;

/// <summary>Request body of the routes that create, update or preview a manual smurf event. The author is never read from the client.</summary>
public class SmurfEventBody
{
    public string Name { get; set; }
    public string From { get; set; }
    public string To { get; set; }
    public List<string> Anchors { get; set; }
    public string Reason { get; set; }
}

/// <summary>Request body of the review route. The author is never read from the client.</summary>
public class SmurfEventReviewBody
{
    public string Decision { get; set; }
    public string Reason { get; set; }
}

/// <summary>
/// Proxy for the matchmaking smurf events moderation API. Matchmaking's status code and JSON body are forwarded
/// unchanged; the acting moderator becomes the author of every write. Field validation (names, anchors, dates)
/// stays with matchmaking.
/// </summary>
[ApiController]
[Route("api/admin/smurf-detection/events")]
[Trace]
public class SmurfEventsModerationController(IAdminRepository adminRepository) : ControllerBase
{
    // Mirrors matchmaking's SMURF_EVENTS_MAX_LIMIT and REVIEWS (smurf-detection api/smurf-events.ts).
    private const int MaxLimit = 100;
    // \z instead of $: .NET's $ also matches before a trailing newline, JavaScript's does not.
    private static readonly Regex IdPattern = new(@"^[A-Za-z0-9_-]{1,64}\z", RegexOptions.Compiled);
    private static readonly FrozenSet<string> Decisions = new[] { "unreviewed", "confirmed", "denied" }.ToFrozenSet();

    private readonly IAdminRepository _adminRepository = adminRepository;

    [HttpGet("")]
    [BearerHasPermissionFilter(Permission = EPermission.SmurfCheckerQuery)]
    public async Task<IActionResult> List([FromQuery] string review, [FromQuery] string origin, [FromQuery] string cursor, [FromQuery] int? limit) =>
        InvalidLimit(limit, MaxLimit) ?? Forward(await _adminRepository.GetSmurfEvents(review, origin, cursor, limit));

    [HttpGet("{id}")]
    [BearerHasPermissionFilter(Permission = EPermission.SmurfCheckerQuery)]
    public async Task<IActionResult> Get([FromRoute] string id) =>
        InvalidId(id) ?? Forward(await _adminRepository.GetSmurfEvent(id));

    [HttpGet("{id}/deny-preview")]
    [BearerHasPermissionFilter(Permission = EPermission.SmurfCheckerQuery)]
    public async Task<IActionResult> DenyPreview([FromRoute] string id) =>
        InvalidId(id) ?? Forward(await _adminRepository.GetSmurfEventDenyPreview(id));

    [HttpPost("preview")]
    [BearerHasPermissionFilter(Permission = EPermission.SmurfCheckerQuery)]
    public async Task<IActionResult> Preview([FromBody] SmurfEventBody body) =>
        MissingBody(body) ?? Forward(await _adminRepository.PreviewSmurfEvent(body.Name, body.From, body.To, body.Anchors));

    [HttpPost("")]
    [BearerHasPermissionFilter(Permission = EPermission.SmurfCheckerAdministration)]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> Create([FromBody] SmurfEventBody body, string actingPlayer) =>
        MissingBody(body) ?? Forward(await _adminRepository.CreateSmurfEvent(body.Name, body.From, body.To, body.Anchors, Trimmed(body.Reason), actingPlayer));

    [HttpPut("{id}")]
    [BearerHasPermissionFilter(Permission = EPermission.SmurfCheckerAdministration)]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> Update([FromRoute] string id, [FromBody] SmurfEventBody body, string actingPlayer) =>
        InvalidId(id) ?? MissingBody(body) ?? Forward(await _adminRepository.UpdateSmurfEvent(id, body.Name, body.From, body.To, body.Anchors, Trimmed(body.Reason), actingPlayer));

    [HttpDelete("{id}")]
    [BearerHasPermissionFilter(Permission = EPermission.SmurfCheckerAdministration)]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> Delete([FromRoute] string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] SmurfReasonBody body, string actingPlayer) =>
        InvalidId(id) ?? Forward(await _adminRepository.DeleteSmurfEvent(id, Trimmed(body?.Reason), actingPlayer));

    [HttpPost("{id}/review")]
    [BearerHasPermissionFilter(Permission = EPermission.SmurfCheckerAdministration)]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> Review([FromRoute] string id, [FromBody] SmurfEventReviewBody body, string actingPlayer) =>
        InvalidId(id) ?? MissingBody(body) ?? InvalidDecision(body.Decision) ?? Forward(await _adminRepository.ReviewSmurfEvent(id, body.Decision, Trimmed(body.Reason), actingPlayer));

    // A blank reason is treated as absent.
    private static string Trimmed(string reason) => string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

    private static IActionResult InvalidId(string id) =>
        id != null && IdPattern.IsMatch(id) ? null : Invalid("id", "invalid event id");

    // The framework already answers an empty or null body before the action runs; this only guards direct calls.
    private static IActionResult MissingBody(object body) =>
        body == null ? Invalid("body", "required") : null;

    private static IActionResult InvalidDecision(string decision) =>
        decision != null && Decisions.Contains(decision) ? null : Invalid("decision", "must be unreviewed, confirmed or denied");
}
