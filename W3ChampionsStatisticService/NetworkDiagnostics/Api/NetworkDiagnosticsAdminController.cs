using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3C.Contracts.Admin.Permission;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.NetworkDiagnostics.Runs;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.NetworkDiagnostics.Api;

/// <summary>
/// C6 admin API. No [ApiController]: errors are always {"error": …}, never ProblemDetails (Deviation 4), so every
/// action checks its own input through <see cref="Answer"/>.
/// BearerHasPermissionFilter writes the acting admin into the "battleTag" action argument; only the action that
/// records who created a run declares it (an unused parameter fails the IDE0060 format gate).
/// </summary>
[Route("api/network-diagnostics")]
[Trace]
public class NetworkDiagnosticsAdminController(INetworkDiagnosticsRunService runService) : ControllerBase
{
    [HttpGet("targets")]
    [BearerHasPermissionFilter(Permission = EPermission.Proxies)]
    public Task<IActionResult> GetTargets() => Answer(runService.GetTargetsAsync);

    /// <summary>
    /// Body: C6 CreateRunRequest. Read by hand instead of [FromBody]: the body is parsed only after the permission
    /// filter let the caller in, is capped, and a malformed, missing or mistyped one answers 400 {"error": …}.
    /// </summary>
    [HttpPost("runs")]
    [BearerHasPermissionFilter(Permission = EPermission.Proxies)]
    public Task<IActionResult> CreateRun([NoTrace] string battleTag) => Answer(async () =>
    {
        var (request, error) = await AdminRequests.ReadJsonBodyAsync<CreateRunRequest>(Request, HttpContext.RequestAborted);
        return error ?? await runService.CreateRunAsync(request, battleTag);
    });

    [HttpGet("runs")]
    [BearerHasPermissionFilter(Permission = EPermission.Proxies)]
    public Task<IActionResult> ListRuns(
        [FromQuery] string createdBy, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int page = 0, [FromQuery] int pageSize = NetDiagPaging.DefaultPageSize) =>
        Answer(() => runService.ListRunsAsync(createdBy, AsUtc(from), AsUtc(to), page, pageSize));

    [HttpGet("runs/{id}")]
    [BearerHasPermissionFilter(Permission = EPermission.Proxies)]
    public Task<IActionResult> GetRun(string id) => Answer(() => runService.GetRunAsync(id));

    [HttpPost("runs/{id}/cancel")]
    [BearerHasPermissionFilter(Permission = EPermission.Proxies)]
    public Task<IActionResult> CancelRun(string id) => Answer(() => runService.CancelRunAsync(id));

    internal static IActionResult Map(NetDiagResult result) => new ObjectResult(result.Body) { StatusCode = result.StatusCode };

    /// <summary>P8: a query value that did not bind answers 400 instead of running the action with a default.</summary>
    private async Task<IActionResult> Answer(Func<Task<NetDiagResult>> action) =>
        Map(AdminRequests.InvalidQuery(ModelState) ?? await action());

    private static DateTime? AsUtc(DateTime? value) => value is { } v ? NetDiagTime.AsUtc(v) : null;
}
