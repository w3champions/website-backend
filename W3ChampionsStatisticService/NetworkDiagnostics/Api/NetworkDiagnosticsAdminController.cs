using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using W3C.Contracts.Admin.Permission;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.NetworkDiagnostics.Results;
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
public class NetworkDiagnosticsAdminController(INetworkDiagnosticsRunService runService, INetworkDiagnosticsQueryService queryService) : ControllerBase
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

    /// <summary>
    /// The C6 shared filter set binds as one complex model: a "battleTag" action parameter would receive the acting
    /// admin from the permission filter instead of the player asked for.
    /// </summary>
    [HttpGet("measurements")]
    [BearerHasPermissionFilter(Permission = EPermission.Proxies)]
    public Task<IActionResult> ListMeasurements(
        [FromQuery] NetDiagQuery filter, [FromQuery] int page = 0, [FromQuery] int pageSize = NetDiagPaging.DefaultPageSize) =>
        Answer(() => queryService.ListMeasurementsAsync(filter, page, pageSize));

    [HttpGet("measurements/{id}")]
    [BearerHasPermissionFilter(Permission = EPermission.Proxies)]
    public Task<IActionResult> GetMeasurement(string id, [FromQuery] bool raw = false) =>
        Answer(() => queryService.GetMeasurementAsync(id, raw));

    internal static IActionResult Map(NetDiagResult result) => new ObjectResult(result.Body) { StatusCode = result.StatusCode };

    /// <summary>
    /// P8: a query value that did not bind answers 400 instead of running the action with a default.
    /// P6: an answer computed from a lookup that stopped at its limit says so in a header, and a query the server
    /// gave up on after <see cref="NetDiagConstants.AdminQueryMaxTime"/> answers 503 instead of an unhandled 500.
    /// </summary>
    private async Task<IActionResult> Answer(Func<Task<NetDiagResult>> action)
    {
        NetDiagResult result;
        try
        {
            result = AdminRequests.InvalidQuery(ModelState) ?? await action();
        }
        catch (MongoException e) when (IsExecutionTimeout(e))
        {
            result = NetDiagResult.Error(503, NetDiagConstants.QueryTimeoutError);
        }
        if (result.Truncated) Response.Headers[NetDiagConstants.TruncatedHeader] = "true";
        return Map(result);
    }

    private const int MaxTimeMsExpiredCode = 50;

    /// <summary>The driver raises MongoExecutionTimeoutException for maxTimeMS; some command paths surface the bare command error.</summary>
    private static bool IsExecutionTimeout(MongoException e) =>
        e is MongoExecutionTimeoutException or MongoCommandException { Code: MaxTimeMsExpiredCode };

    private static DateTime? AsUtc(DateTime? value) => value is { } v ? NetDiagTime.AsUtc(v) : null;
}
