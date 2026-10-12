using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using W3ChampionsStatisticService.NetworkDiagnostics.Api;
using W3ChampionsStatisticService.NetworkDiagnostics.GeoIp;
using W3ChampionsStatisticService.NetworkDiagnostics.Persistence;
using W3ChampionsStatisticService.NetworkDiagnostics.Results;
using W3ChampionsStatisticService.NetworkDiagnostics.Runs;

namespace W3ChampionsStatisticService.NetworkDiagnostics.Submission;

public interface INetworkDiagnosticsSubmissionService
{
    /// <param name="remoteIp">The peer's IPv4 address, or null when it has none; used when the body reports no public_ipv4.</param>
    Task<NetDiagResult> SubmitAsync(string routeMeasurementId, string actingPlayer, string token, NetDiagSubmission dto, string remoteIp);
}

/// <summary>Spec §5.5. Not intercepted by tracing: it receives the measurement token (A10).</summary>
public sealed class NetworkDiagnosticsSubmissionService(
    INetworkDiagnosticsAssignmentRepository assignments,
    INetworkDiagnosticsRunRepository runs,
    INetworkDiagnosticsResultRepository results,
    IGeoIpLookup geoIp,
    TimeProvider time,
    ILogger<NetworkDiagnosticsSubmissionService> log) : INetworkDiagnosticsSubmissionService
{
    private static readonly IReadOnlyDictionary<string, ClientGeo> NoGeo = new Dictionary<string, ClientGeo>();

    private static NetDiagResult Forbidden => NetDiagResult.Error(403, "forbidden");

    public async Task<NetDiagResult> SubmitAsync(string routeMeasurementId, string actingPlayer, string token, NetDiagSubmission dto, string remoteIp)
    {
        // Ids are compared as they are (ordinal). Nothing below runs on a body that is over a cap.
        var invalid = SubmissionValidator.ValidateSubmission(dto, routeMeasurementId);
        if (invalid != null) return NetDiagResult.Error(400, invalid);

        // A10: owner check plus a constant-time compare against the stored plaintext token, before any write and
        // before the 409s, so a caller without the token learns nothing about the slot.
        var existing = await assignments.GetAsync(routeMeasurementId);
        if (existing == null || existing.BattleTag != actingPlayer || !MeasurementTokens.Matches(existing.Token, token)) return Forbidden;

        var now = time.GetUtcNow().UtcDateTime;
        var rejection = Rejection(existing, now);
        if (rejection != null) return rejection;

        // Exactly once: only one caller moves the assignment from Pending to Submitted.
        var assignment = await assignments.TrySubmitAsync(routeMeasurementId, actingPlayer, now);
        // Lost a race with a concurrent submit (or the deadline passed in between): re-read for the precise 409.
        if (assignment == null) return Rejection(await assignments.GetAsync(routeMeasurementId), now) ?? NetDiagResult.Error(409, "already_submitted");

        try
        {
            var clientIp = ResultBuilder.ChooseClientIp(dto, remoteIp);
            var geo = LookupGeo(ResultBuilder.LookupIps(dto, clientIp), assignment);
            await results.UpsertIpInfoAsync(ResultBuilder.IpInfoRows(assignment.RunId, geo, now));
            await results.InsertResultAsync(ResultBuilder.Build(dto, assignment, now, clientIp, geo));
        }
        catch (Exception e)
        {
            // The assignment is already Submitted, so the launcher's retry gets 409: this one result is lost
            // (spec §5.5). There is no rollback; the log line makes the gap visible.
            log.LogError(e, "Network diagnostics: storing measurement {MeasurementId} of run {RunId} for {BattleTag} failed after it was marked submitted; the result is lost",
                assignment.Id, assignment.RunId, assignment.BattleTag);
            return NetDiagResult.Error(500, "internal_error");
        }

        try
        {
            await runs.IncrementCountersAsync(assignment.RunId, submitted: 1);
        }
        catch (Exception e)
        {
            // The result is stored. A 500 here would send the launcher into a retry that can only end in 409.
            log.LogError(e, "Network diagnostics: measurement {MeasurementId} is stored but the submitted counter of run {RunId} was not incremented",
                assignment.Id, assignment.RunId);
        }

        log.LogInformation("Network diagnostics: measurement {MeasurementId} of run {RunId} submitted by {BattleTag}", assignment.Id, assignment.RunId, assignment.BattleTag);
        return NetDiagResult.Created(new { measurementId = assignment.Id });
    }

    /// <summary>GeoIP is enrichment only: when it is unavailable the result is stored without geo. Never null.</summary>
    private IReadOnlyDictionary<string, ClientGeo> LookupGeo(List<string> ips, NetworkDiagnosticAssignment assignment)
    {
        try
        {
            return geoIp.Lookup(ips) ?? NoGeo;
        }
        catch (Exception e)
        {
            log.LogWarning(e, "Network diagnostics: GeoIP lookup failed for measurement {MeasurementId} of run {RunId}; storing it without geo", assignment.Id, assignment.RunId);
            return NoGeo;
        }
    }

    /// <summary>Null when the assignment can still be submitted.</summary>
    private static NetDiagResult Rejection(NetworkDiagnosticAssignment a, DateTime now)
    {
        if (a == null) return Forbidden;
        if (a.Status == AssignmentStatus.Submitted) return NetDiagResult.Error(409, "already_submitted");
        if (a.Status != AssignmentStatus.Pending || a.DeliverBy <= now) return NetDiagResult.Error(409, "expired");
        return null;
    }
}
