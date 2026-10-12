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
    /// <param name="readBody">
    /// Reads and parses the request body; returns the submission or the text of a 400. It is called only after the
    /// caller proved to own the measurement and to hold its token, so nothing is read or inflated for anyone else.
    /// </param>
    /// <param name="remoteIp">The peer's public IPv4 address, or null; used when the body reports no public_ipv4.</param>
    Task<NetDiagResult> SubmitAsync(
        string routeMeasurementId,
        string actingPlayer,
        string token,
        Func<Task<(NetDiagSubmission Dto, string Error)>> readBody,
        string remoteIp);
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
    private static NetDiagResult InternalError => NetDiagResult.Error(500, "internal_error");

    public async Task<NetDiagResult> SubmitAsync(
        string routeMeasurementId,
        string actingPlayer,
        string token,
        Func<Task<(NetDiagSubmission Dto, string Error)>> readBody,
        string remoteIp)
    {
        // A10: owner check plus a constant-time compare against the stored plaintext token. It comes before the body
        // is read, before the 409s and before any write, so a caller without the token costs one lookup and learns
        // nothing about the slot. The id is used as it came in the URL (ordinal).
        if (string.IsNullOrEmpty(token)) return Forbidden;
        var existing = await assignments.GetAsync(routeMeasurementId);
        if (existing == null || existing.BattleTag != actingPlayer || !MeasurementTokens.Matches(existing.Token, token)) return Forbidden;

        var (dto, bodyError) = await readBody();
        if (bodyError != null) return NetDiagResult.Error(400, bodyError);
        // Nothing below runs on a body that is over a cap.
        var invalid = SubmissionValidator.ValidateSubmission(dto, routeMeasurementId);
        if (invalid != null) return NetDiagResult.Error(400, invalid);

        var now = time.GetUtcNow().UtcDateTime;
        var rejection = Rejection(existing, now);
        if (rejection != null) return rejection;

        // Everything that can fail on the content is done while the slot is still Pending: a failure here is a 500
        // the launcher can retry. `now` is also what the conditional update stores as SubmittedAt.
        BuiltResult built;
        List<NetworkDiagnosticIpInfo> ipInfo;
        try
        {
            var clientIp = ResultBuilder.ChooseClientIp(dto, remoteIp);
            var geo = geoIp.Lookup(ResultBuilder.LookupIps(dto, clientIp)) ?? NoGeo;
            ipInfo = ResultBuilder.IpInfoRows(existing.RunId, geo, now);
            built = ResultBuilder.Build(dto, existing, now, clientIp, geo);
        }
        catch (Exception e)
        {
            log.LogError(e, "Network diagnostics: building the result of measurement {MeasurementId} of run {RunId} for {BattleTag} failed; nothing was written and the slot is still open",
                existing.Id, existing.RunId, existing.BattleTag);
            return InternalError;
        }

        // Exactly once: only one caller moves the assignment from Pending to Submitted.
        NetworkDiagnosticAssignment assignment;
        try
        {
            assignment = await assignments.TrySubmitAsync(routeMeasurementId, actingPlayer, now);
        }
        catch (Exception e)
        {
            // The update may have been applied although its reply was lost; then the retry gets 409 and the result
            // is gone. Logged so that case is visible.
            log.LogError(e, "Network diagnostics: marking measurement {MeasurementId} of run {RunId} for {BattleTag} submitted failed; if the update was applied the result is lost",
                existing.Id, existing.RunId, existing.BattleTag);
            return InternalError;
        }
        // Lost a race with a concurrent submit (or the deadline passed in between): re-read for the precise 409.
        if (assignment == null) return Rejection(await assignments.GetAsync(routeMeasurementId), now) ?? NetDiagResult.Error(409, "already_submitted");

        try
        {
            await results.InsertResultAsync(built);
        }
        catch (Exception e)
        {
            // The assignment is already Submitted, so the launcher's retry gets 409: this one result is lost
            // (spec §5.5). There is no rollback; the log line makes the gap visible.
            log.LogError(e, "Network diagnostics: storing measurement {MeasurementId} of run {RunId} for {BattleTag} failed after it was marked submitted; the result is lost",
                assignment.Id, assignment.RunId, assignment.BattleTag);
            return InternalError;
        }

        // The result is stored. A 500 from here on would send the launcher into a retry that can only end in 409.
        try
        {
            await results.UpsertIpInfoAsync(ipInfo);
        }
        catch (Exception e)
        {
            log.LogError(e, "Network diagnostics: measurement {MeasurementId} is stored but the IP info of run {RunId} was not", assignment.Id, assignment.RunId);
        }

        try
        {
            await runs.IncrementCountersAsync(assignment.RunId, submitted: 1);
        }
        catch (Exception e)
        {
            log.LogError(e, "Network diagnostics: measurement {MeasurementId} is stored but the submitted counter of run {RunId} was not incremented",
                assignment.Id, assignment.RunId);
        }

        log.LogInformation("Network diagnostics: measurement {MeasurementId} of run {RunId} submitted by {BattleTag}", assignment.Id, assignment.RunId, assignment.BattleTag);
        return NetDiagResult.Created(new { measurementId = assignment.Id });
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
