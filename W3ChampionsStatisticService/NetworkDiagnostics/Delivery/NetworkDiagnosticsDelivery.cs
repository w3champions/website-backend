using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using W3ChampionsStatisticService.Hubs;
using W3ChampionsStatisticService.NetworkDiagnostics.Persistence;
using W3ChampionsStatisticService.NetworkDiagnostics.Runs;

namespace W3ChampionsStatisticService.NetworkDiagnostics.Delivery;

/// <summary>C4 hub payload (SignalR default JSON = camelCase).</summary>
public record NetworkDiagnosticsRequestDto(string MeasurementId, string Token, DateTime DeliverBy, NetworkDiagnosticsSpecDto Spec)
{
    // Replaces the generated record ToString, which would print Token (A10).
    public override string ToString() =>
        $"NetworkDiagnosticsRequestDto {{ MeasurementId = {MeasurementId}, DeliverBy = {DeliverBy:o} }}";
}

public record NetworkDiagnosticsSpecDto(
    List<string> Methods,
    bool AllowDuringGame,
    int WindowSeconds,
    List<int> FloNodeIds,
    bool AllFloNodes,
    List<RunEndpoint> Endpoints,
    bool IncludeDefaultGateway);

public static class DeliveryRules
{
    public static DateTime FleetDeliverBy(NetworkDiagnosticRun run) => run.WindowEnd!.Value + NetDiagConstants.FleetSubmitGrace;

    /// <summary>
    /// Pending, inside [DeliverAfter, DeliverBy), run not cancelled. A fleet assignment gets its *first* delivery only
    /// while the window is open; after WindowEnd only re-pushes of an already delivered one (Deviation 11, contract A6).
    /// </summary>
    public static bool IsDeliverable(NetworkDiagnosticAssignment a, NetworkDiagnosticRun run, DateTime now)
    {
        if (run.CancelledAt != null || a.Status != AssignmentStatus.Pending) return false;
        if (now < a.DeliverAfter || now >= a.DeliverBy) return false;
        return !run.IsFleet || a.DeliveredAt != null || (run.WindowEnd is { } end && now < end);
    }

    /// <summary>
    /// True for a Pending assignment that no later push can deliver: its run does not exist (the run insert failed
    /// after the assignment insert), its run is cancelled (the cancel's sweep failed), or it belongs to a fleet run
    /// whose window closed before its first delivery (A6; WindowEnd itself is outside the window).
    /// A missing run counts only once the assignment is due for <see cref="NetDiagConstants.OrphanGrace"/>: a run
    /// that is being created has its assignments stored a moment before the run itself.
    /// </summary>
    public static bool NeverDeliverable(NetworkDiagnosticAssignment a, NetworkDiagnosticRun run, DateTime now)
    {
        if (a.Status != AssignmentStatus.Pending) return false;
        if (run == null) return a.DeliverAfter + NetDiagConstants.OrphanGrace <= now;
        if (run.CancelledAt != null) return true;
        return run.IsFleet && a.DeliveredAt == null && run.WindowEnd is { } end && now >= end;
    }
}

public interface INetworkDiagnosticsDelivery
{
    /// <summary>Sends to every connection of the BattleTag. False when the user is offline.</summary>
    Task<bool> PushAsync(NetworkDiagnosticAssignment assignment, NetworkDiagnosticRun run);

    /// <summary>
    /// The one push loop (on-connect and dispatcher): looks each run up once, pushes every assignment that
    /// <see cref="DeliveryRules.IsDeliverable"/> allows, and marks first deliveries. An assignment that can never be
    /// pushed again (<see cref="DeliveryRules.NeverDeliverable"/>) is expired, so it does not come back in the
    /// dispatcher's next batch. A failure for one assignment is logged and the rest are still pushed. Returns how
    /// many assignments reached at least one connection.
    /// </summary>
    Task<int> PushDueAsync(IReadOnlyCollection<NetworkDiagnosticAssignment> assignments, DateTime now);

    /// <summary>Late-joiner assignment for open fleet runs, then push of every due assignment.</summary>
    Task OnUserConnectedAsync(string battleTag);

    /// <summary>
    /// Cancel-race guard (§5.4) for callers that insert assignments into a run: re-reads the run and, when it was
    /// cancelled meanwhile, expires its Pending assignments and counts them. True when the run is cancelled.
    /// </summary>
    Task<bool> ExpireIfCancelledAsync(string runId);
}

/// <summary>Registered with plain AddSingleton (no tracing interception): it handles tokens. Never log a token.</summary>
public sealed class NetworkDiagnosticsDelivery(
    IHubContext<WebsiteBackendHub> hub,
    ConnectionMapping connections,
    INetworkDiagnosticsRunRepository runs,
    INetworkDiagnosticsAssignmentRepository assignments,
    TimeProvider time,
    IRandom random,
    ILogger<NetworkDiagnosticsDelivery> log) : INetworkDiagnosticsDelivery
{
    public async Task<bool> PushAsync(NetworkDiagnosticAssignment assignment, NetworkDiagnosticRun run)
    {
        var connectionIds = connections.GetConnectionId(assignment.BattleTag).ToList();
        if (connectionIds.Count == 0) return false;

        await hub.Clients.Clients(connectionIds).SendAsync(NetDiagConstants.HubEventName, ToDto(assignment, run));

        if (await assignments.TryMarkDeliveredAsync(assignment.Id, Now()))
        {
            await runs.IncrementCountersAsync(run.Id, delivered: 1);
        }
        return true;
    }

    public async Task<int> PushDueAsync(IReadOnlyCollection<NetworkDiagnosticAssignment> due, DateTime now)
    {
        var runCache = new Dictionary<string, NetworkDiagnosticRun>();
        var swept = new HashSet<string>();
        var pushed = 0;
        foreach (var assignment in due)
        {
            try
            {
                if (!runCache.TryGetValue(assignment.RunId, out var run))
                {
                    run = await runs.GetAsync(assignment.RunId);
                    runCache[assignment.RunId] = run;
                }
                if (DeliveryRules.NeverDeliverable(assignment, run, now))
                {
                    // One sweep per run and call covers every such assignment of the run, in this batch or not.
                    if (swept.Add(assignment.RunId)) await ExpireNeverDeliverableAsync(assignment.RunId, run);
                    continue;
                }
                if (run != null && DeliveryRules.IsDeliverable(assignment, run, now) && await PushAsync(assignment, run))
                {
                    pushed++;
                }
            }
            catch (Exception e)
            {
                // Measurement, run and BattleTag only: the assignment's token must never reach a log line.
                log.LogWarning(e, "Network diagnostics: push of measurement {MeasurementId} (run {RunId}) to {BattleTag} failed",
                    assignment.Id, assignment.RunId, assignment.BattleTag);
            }
        }
        return pushed;
    }

    public async Task OnUserConnectedAsync(string battleTag)
    {
        var now = Now();
        await AssignToOpenFleetRunsAsync(battleTag, now);
        await PushDueAsync(await assignments.DueForAsync(battleTag, now), now);
    }

    /// <summary>Never throws: a late-joiner failure must not cost the user the push of assignments that are already due.</summary>
    private async Task AssignToOpenFleetRunsAsync(string battleTag, DateTime now)
    {
        List<NetworkDiagnosticRun> fleetRuns;
        try
        {
            fleetRuns = await runs.ActiveFleetRunsAsync(now);
        }
        catch (Exception e)
        {
            log.LogWarning(e, "Network diagnostics: reading the open fleet runs for late joiner {BattleTag} failed", battleTag);
            return;
        }

        foreach (var run in fleetRuns)
        {
            try
            {
                await TryAssignLateJoinerAsync(run, battleTag, now);
            }
            catch (Exception e)
            {
                log.LogWarning(e, "Network diagnostics: late-joiner assignment of {BattleTag} to run {RunId} failed", battleTag, run.Id);
            }
        }
    }

    private async Task TryAssignLateJoinerAsync(NetworkDiagnosticRun run, string battleTag, DateTime now)
    {
        if (await assignments.ExistsAsync(run.Id, battleTag)) return;
        if (!AudiencePlanner.ShouldAssignLateJoiner(run, now, random)) return; // also false for a cancelled run

        var slot = AudiencePlanner.RandomSlot(now, run.WindowEnd!.Value, random);
        var assignment = AssignmentFactory.Create(run.Id, battleTag, slot, DeliveryRules.FleetDeliverBy(run));
        if (!await assignments.TryInsertAsync(assignment)) return;

        try
        {
            await runs.IncrementCountersAsync(run.Id, assigned: 1);
            log.LogInformation("Network diagnostics: late joiner {BattleTag} assigned to run {RunId} at {Slot:o}", battleTag, run.Id, slot);
        }
        finally
        {
            // In a finally: the assignment exists, so the cancel re-check must run even when the counter update failed.
            await ExpireIfCancelledMeanwhileAsync(run.Id, battleTag);
        }
    }

    /// <summary>
    /// Closes the cancel race: a cancel sets CancelledAt and then expires the run's Pending assignments, so an insert
    /// that lands after that sweep would stay Pending in a cancelled run. Re-reading the run after the insert sees
    /// either "not cancelled" (the cancel's own sweep comes later and covers the new assignment) or "cancelled"
    /// (expire here). Each side counts only the documents it modified, so Counters.Expired stays exact.
    /// </summary>
    private async Task ExpireIfCancelledMeanwhileAsync(string runId, string battleTag)
    {
        if (!await ExpireIfCancelledAsync(runId)) return;
        log.LogInformation("Network diagnostics: run {RunId} was cancelled while {BattleTag} joined late; the assignment is expired", runId, battleTag);
    }

    public async Task<bool> ExpireIfCancelledAsync(string runId)
    {
        var current = await runs.GetAsync(runId);
        if (current?.CancelledAt == null) return false;

        await ExpireAndCountAsync(runId, assignments.ExpireAllPendingAsync);
        return true;
    }

    /// <summary>
    /// What <see cref="DeliveryRules.NeverDeliverable"/> found, expired for the whole run: every Pending assignment
    /// of a missing or cancelled run; of a fleet run past its window only the never delivered ones, because a
    /// delivered one may still be submitted until DeliverBy.
    /// </summary>
    private async Task ExpireNeverDeliverableAsync(string runId, NetworkDiagnosticRun run)
    {
        if (run == null)
        {
            // No run, so no counter. Run id only: the assignments carry tokens.
            var orphans = await assignments.ExpireAllPendingAsync(runId);
            log.LogWarning("Network diagnostics: expired {Expired} assignments of run {RunId}, which does not exist", orphans, runId);
            return;
        }
        var expired = await ExpireAndCountAsync(runId, run.CancelledAt != null ? assignments.ExpireAllPendingAsync : assignments.ExpireUndeliveredAsync);
        if (expired > 0)
        {
            log.LogInformation("Network diagnostics: expired {Expired} assignments of run {RunId} that could no longer be delivered", expired, runId);
        }
    }

    /// <summary>
    /// The one place that expires a run's assignments outside the DeliverBy sweep and puts them on its expired
    /// counter. The update reports only the documents it changed, so concurrent or repeated sweeps never count an
    /// assignment twice.
    /// </summary>
    private async Task<int> ExpireAndCountAsync(string runId, Func<string, Task<int>> expire)
    {
        var expired = await expire(runId);
        if (expired > 0)
        {
            await runs.IncrementCountersAsync(runId, expired: expired);
        }
        return expired;
    }

    private static NetworkDiagnosticsRequestDto ToDto(NetworkDiagnosticAssignment a, NetworkDiagnosticRun run) => new(
        a.Id,
        a.Token, // A10: the stored token, identical on every re-push
        NetDiagTime.AsUtc(a.DeliverBy), // the launcher needs an explicit UTC instant ("…Z")
        new NetworkDiagnosticsSpecDto(
            run.Methods ?? [],
            run.AllowDuringGame,
            run.WindowSeconds,
            run.Targets?.FloNodeIds ?? [],
            run.Targets?.AllFloNodes ?? false,
            run.ResolvedEndpoints ?? [],
            run.Targets?.IncludeDefaultGateway ?? false));

    private DateTime Now() => time.GetUtcNow().UtcDateTime;
}
