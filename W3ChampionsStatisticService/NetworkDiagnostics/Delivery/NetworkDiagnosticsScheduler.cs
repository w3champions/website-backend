using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using W3ChampionsStatisticService.Hubs;
using W3ChampionsStatisticService.NetworkDiagnostics.Persistence;
using W3ChampionsStatisticService.NetworkDiagnostics.Runs;

namespace W3ChampionsStatisticService.NetworkDiagnostics.Delivery;

public interface INetworkDiagnosticsScheduler
{
    Task TickAsync(CancellationToken cancellationToken);
}

/// <summary>
/// One dispatcher tick (spec §5.3). All state lives in Mongo; WB is single-instance.
/// Registered with plain AddSingleton (no tracing interception): it handles assignments that carry tokens (A10).
/// </summary>
public sealed class NetworkDiagnosticsScheduler(
    ConnectionMapping connections,
    INetworkDiagnosticsRunRepository runs,
    INetworkDiagnosticsAssignmentRepository assignments,
    INetworkDiagnosticsDelivery delivery,
    TimeProvider time,
    IRandom random,
    ILogger<NetworkDiagnosticsScheduler> log) : INetworkDiagnosticsScheduler
{
    public const int PushBatchLimit = 500;

    public async Task TickAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var online = connections.GetOnlineBattleTags();
        await RunStep("push", () => PushDueAsync(online, now), cancellationToken);
        await RunStep("top-up", () => TopUpAsync(online, now, cancellationToken), cancellationToken);
        await RunStep("expire", () => ExpireAsync(now, cancellationToken), cancellationToken);
    }

    /// <summary>A failing step is logged and the next one still runs. Cancellation is never swallowed.</summary>
    private async Task RunStep(string name, Func<Task> step, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            await step();
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            log.LogError(e, "Network diagnostics dispatcher step {Step} failed", name);
        }
    }

    private async Task PushDueAsync(List<string> online, DateTime now)
    {
        if (online.Count == 0) return;
        var due = await assignments.DueUndeliveredAsync(online, now, PushBatchLimit);
        if (due.Count == 0) return;
        // P4: the delivery service owns the push loop (run lookup, deliverability, per-assignment failure handling).
        await delivery.PushDueAsync(due, now);
    }

    private async Task TopUpAsync(List<string> online, DateTime now, CancellationToken ct)
    {
        if (online.Count == 0) return;
        foreach (var run in await runs.ActiveFleetRunsAsync(now))
        {
            ct.ThrowIfCancellationRequested();
            // A6: top-up stops at WindowEnd even though DeliverBy runs 15 min longer.
            if (run.Audience.Kind != AudienceKind.SampleCount || run.CancelledAt != null
                || run.WindowStart > now || run.WindowEnd is not { } windowEnd || windowEnd <= now) continue;

            var pending = await assignments.CountPendingUndeliveredAsync(run.Id, online, now);
            var need = AudiencePlanner.TopUpNeed(run.Audience.Count!.Value, run.Counters.Delivered, pending);
            if (need == 0) continue;

            var assigned = (await assignments.AssignedBattleTagsAsync(run.Id)).ToHashSet(StringComparer.Ordinal);
            var candidates = online.Where(tag => !assigned.Contains(tag)).ToList();
            var inserted = 0;
            foreach (var tag in AudiencePlanner.PickRandom(candidates, need, random))
            {
                ct.ThrowIfCancellationRequested();
                var slot = AudiencePlanner.RandomSlot(now, windowEnd, random);
                if (await assignments.TryInsertAsync(AssignmentFactory.Create(run.Id, tag, slot, DeliveryRules.FleetDeliverBy(run))))
                {
                    inserted++;
                }
            }
            if (inserted > 0)
            {
                await runs.IncrementCountersAsync(run.Id, assigned: inserted);
                log.LogInformation("Network diagnostics: topped up run {RunId} with {Count} assignments", run.Id, inserted);
            }
        }
    }

    private async Task ExpireAsync(DateTime now, CancellationToken ct)
    {
        foreach (var (runId, count) in await assignments.ExpireOverdueAsync(now))
        {
            ct.ThrowIfCancellationRequested();
            await runs.IncrementCountersAsync(runId, expired: count);
        }
    }
}
