using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Hubs;
using W3ChampionsStatisticService.NetworkDiagnostics.Api;
using W3ChampionsStatisticService.NetworkDiagnostics.Delivery;
using W3ChampionsStatisticService.NetworkDiagnostics.Persistence;
using W3ChampionsStatisticService.NetworkDiagnostics.Results;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.NetworkDiagnostics.Runs;

public interface INetworkDiagnosticsRunService
{
    Task<NetDiagResult> GetTargetsAsync();
    Task<NetDiagResult> CreateRunAsync(CreateRunRequest request, string createdBy);
    /// <summary><paramref name="from"/> (inclusive) and <paramref name="to"/> (exclusive) are UTC; page is zero-based (A5).</summary>
    Task<NetDiagResult> ListRunsAsync(string createdBy, DateTime? from, DateTime? to, int page, int pageSize);
    Task<NetDiagResult> GetRunAsync(string id);
    Task<NetDiagResult> CancelRunAsync(string id);
}

/// <summary>
/// Registered with plain AddTransient (no tracing interception): it creates assignments and so handles measurement
/// tokens (A10). Never log an assignment's token; log run, measurement and BattleTag only.
/// </summary>
public sealed class NetworkDiagnosticsRunService(
    INetworkDiagnosticsRunRepository runs,
    INetworkDiagnosticsAssignmentRepository assignments,
    INetworkDiagnosticsResultRepository results,
    INetworkDiagnosticsDelivery delivery,
    IFloNodeSource floNodes,
    IPlayerRepository players,
    IPersonalSettingsRepository personalSettings,
    NetDiagTargetsConfig config,
    ConnectionMapping connections,
    TimeProvider time,
    IRandom random,
    ILogger<NetworkDiagnosticsRunService> log) : INetworkDiagnosticsRunService
{
    private static readonly NetDiagResult MatchmakingDown = NetDiagResult.Error(502, "matchmaking_unavailable");

    public async Task<NetDiagResult> GetTargetsAsync()
    {
        var nodes = await TryGetFloNodesAsync();
        if (nodes == null) return MatchmakingDown;
        var floDtos = nodes.Select(n => new FloNodeDto(n.Id, n.Name, n.Location, n.CountryId, IsDisabled(n))).ToList();
        return NetDiagResult.Ok(new TargetsDto(floDtos, config.CoreServers, config.Baselines));
    }

    public async Task<NetDiagResult> CreateRunAsync(CreateRunRequest request, string createdBy)
    {
        var normalized = CreateRunValidator.Normalize(request);
        var error = CreateRunValidator.Validate(normalized);
        if (error != null) return NetDiagResult.Error(400, error);

        var targets = normalized.Targets;
        var unknownKeys = config.UnknownKeys(targets);
        if (unknownKeys.Count > 0) return new NetDiagResult(400, new { error = "unknown_targets", targetKeys = unknownKeys });

        var floCount = 0;
        if (targets.FloNodeIds.Count > 0 || targets.AllFloNodes)
        {
            var nodes = await TryGetFloNodesAsync();
            if (nodes == null) return MatchmakingDown;
            var known = nodes.Select(n => n.Id).ToHashSet();
            var unknownNodes = targets.FloNodeIds.Where(id => !known.Contains(id)).ToList();
            if (unknownNodes.Count > 0) return new NetDiagResult(400, new { error = "unknown_flo_nodes", floNodeIds = unknownNodes });
            floCount = targets.AllFloNodes ? nodes.Count(n => !IsDisabled(n)) : targets.FloNodeIds.Count;
        }

        var endpoints = config.ResolveEndpoints(targets);
        var targetCount = floCount + endpoints.Count + (targets.IncludeDefaultGateway ? 1 : 0);
        if (targetCount > NetDiagConstants.MaxTargetsPerRun) return NetDiagResult.Error(400, $"too many targets (max {NetDiagConstants.MaxTargetsPerRun})");

        if (normalized.Audience.Kind == AudienceKind.Users)
        {
            var unknownTags = await UnknownBattleTagsAsync(normalized.Audience.BattleTags);
            if (unknownTags.Count > 0) return new NetDiagResult(400, new { error = "unknown_battletags", battleTags = unknownTags });
        }

        var now = Now();
        var isFleet = normalized.Audience.Kind != AudienceKind.Users;
        var run = new NetworkDiagnosticRun
        {
            Id = Guid.NewGuid().ToString(),
            CreatedAt = now,
            CreatedBy = createdBy,
            Audience = normalized.Audience,
            Targets = targets,
            ResolvedEndpoints = endpoints,
            Methods = normalized.Methods,
            AllowDuringGame = normalized.AllowDuringGame,
            WindowSeconds = normalized.WindowSeconds!.Value,
            WindowStart = isFleet ? now : null,
            WindowEnd = isFleet ? now + NetDiagConstants.FleetWindow : null,
            ExpiresAt = now + NetDiagConstants.ExplicitDeliveryWindow + NetDiagConstants.Retention,
        };

        var created = isFleet ? FleetAssignments(run, now) : UserAssignments(run, now);
        run = run with { Counters = new RunCounters { Assigned = created.Count } };

        // Assignments first: a crash in between leaves orphans that expire, never a run that claims assignments it lacks.
        await assignments.InsertManyAsync(created);
        await runs.InsertAsync(run);
        log.LogInformation("Network diagnostics run {RunId} created by {CreatedBy}: {Kind}, {Assigned} assignments, {Targets} targets",
            run.Id, createdBy, run.Audience.Kind, created.Count, targetCount);

        // Fleet slots are spread over the window and pushed by the dispatcher when due. An explicit audience is due
        // now: push the users that are online; the others get it when they connect.
        if (!isFleet) await PushToOnlineUsersAsync(run, created, now);
        return NetDiagResult.Created(RunDto.From(run));
    }

    /// <summary>
    /// Never throws: the run is stored, and a failed answer would make the admin create it a second time. Whatever is
    /// not pushed here is pushed by the dispatcher's next tick or on the user's next connect.
    /// </summary>
    private async Task PushToOnlineUsersAsync(NetworkDiagnosticRun run, List<NetworkDiagnosticAssignment> created, DateTime now)
    {
        var online = created.Where(a => connections.IsUserOnline(a.BattleTag)).ToList();
        if (online.Count == 0) return;
        try
        {
            await delivery.PushDueAsync(online, now);
        }
        catch (Exception e)
        {
            log.LogWarning(e, "Network diagnostics: first push of run {RunId} failed; the dispatcher retries", run.Id);
        }
    }

    private static List<NetworkDiagnosticAssignment> UserAssignments(NetworkDiagnosticRun run, DateTime now) =>
        run.Audience.BattleTags
            .Select(tag => AssignmentFactory.Create(run.Id, tag, now, now + NetDiagConstants.ExplicitDeliveryWindow))
            .ToList();

    private List<NetworkDiagnosticAssignment> FleetAssignments(NetworkDiagnosticRun run, DateTime now) =>
        AudiencePlanner.PickInitial(run.Audience, connections.GetOnlineBattleTags(), random)
            .Select(tag => AssignmentFactory.Create(run.Id, tag, AudiencePlanner.RandomSlot(now, run.WindowEnd!.Value, random), DeliveryRules.FleetDeliverBy(run)))
            .ToList();

    private async Task<List<string>> UnknownBattleTagsAsync(List<string> tags)
    {
        var withStats = await players.GetPlayerBattleTagsAsync(tags) ?? [];
        var withSettings = await personalSettings.LoadMany(tags.ToArray()) ?? [];
        var known = withStats.Keys.Concat(withSettings.Select(s => s.Id)).ToHashSet(StringComparer.Ordinal);
        return tags.Where(t => !known.Contains(t)).ToList();
    }

    /// <summary>
    /// Null when matchmaking is unreachable, fails or times out (HttpClient reports its own timeout as a
    /// TaskCanceledException caused by a TimeoutException).
    /// </summary>
    private async Task<List<FloNodeInfo>> TryGetFloNodesAsync()
    {
        try
        {
            return await floNodes.GetFloNodesAsync() ?? [];
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException { InnerException: TimeoutException })
        {
            log.LogWarning(e, "Network diagnostics: matchmaking flo node list unavailable");
            return null;
        }
    }

    private static bool IsDisabled(FloNodeInfo node) => node.Settings?.IsDisabled ?? false;

    public async Task<NetDiagResult> ListRunsAsync(string createdBy, DateTime? from, DateTime? to, int page, int pageSize)
    {
        var invalid = NetDiagPaging.Validate(page, pageSize);
        if (invalid != null) return invalid;
        var (items, total) = await runs.ListAsync(createdBy, from, to, page, pageSize);
        return NetDiagResult.Ok(new PageDto<RunDto>(items.Select(RunDto.From).ToList(), total));
    }

    public async Task<NetDiagResult> GetRunAsync(string id)
    {
        var run = await runs.GetAsync(id);
        if (run == null) return NetDiagResult.Error(404, "not_found");
        var list = await assignments.ForRunAsync(id);
        var submittedIds = list.Where(a => a.Status == AssignmentStatus.Submitted).Select(a => a.Id).ToList();
        var geo = submittedIds.Count == 0 ? new Dictionary<string, ClientGeo>() : await results.ClientGeoByMeasurementAsync(submittedIds);
        var dtos = list.Select(a => new AssignmentDto(a.Id, a.BattleTag, a.Status, a.DeliverAfter, a.DeliverBy, a.DeliveredAt, a.SubmittedAt,
            geo.GetValueOrDefault(a.Id))).ToList();
        return NetDiagResult.Ok(new RunDetailDto(RunDto.From(run), dtos));
    }

    public async Task<NetDiagResult> CancelRunAsync(string id)
    {
        // Order matters: CancelledAt is set first, the Pending sweep comes second. Late-joiner and top-up inserts
        // re-read the run after inserting (INetworkDiagnosticsDelivery.ExpireIfCancelledAsync): they either see the
        // cancel and expire their own assignment, or they inserted before this sweep, which then covers them.
        var cancelled = await runs.TryCancelAsync(id, Now());
        if (cancelled == null)
        {
            return await runs.GetAsync(id) == null
                ? NetDiagResult.Error(404, "not_found")
                : NetDiagResult.Error(409, "already_cancelled");
        }
        var expired = await assignments.ExpireAllPendingAsync(id);
        if (expired > 0) await runs.IncrementCountersAsync(id, expired: expired);
        log.LogInformation("Network diagnostics run {RunId} cancelled; {Expired} pending assignments expired", id, expired);
        return NetDiagResult.Ok(RunDto.From(await runs.GetAsync(id) ?? cancelled));
    }

    private DateTime Now() => time.GetUtcNow().UtcDateTime;
}
