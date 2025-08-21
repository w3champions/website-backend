using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using W3C.Domain.Rewards.Abstractions;
using W3C.Domain.Rewards.Entities;
using W3C.Domain.Rewards.Events;
using W3C.Domain.Rewards.Repositories;
using W3ChampionsStatisticService.Rewards.Providers.Patreon;

namespace W3ChampionsStatisticService.Rewards.Services;

public class PatreonDriftDetectionService(
    PatreonApiClient patreonApiClient,
    IRewardAssignmentRepository assignmentRepository,
    IRewardService rewardService,
    IPatreonAccountLinkRepository patreonLinkRepository)
{
    private readonly PatreonApiClient _patreonApiClient = patreonApiClient;
    private readonly IRewardAssignmentRepository _assignmentRepository = assignmentRepository;
    private readonly IRewardService _rewardService = rewardService;
    private readonly IPatreonAccountLinkRepository _patreonLinkRepository = patreonLinkRepository;
    private const string ProviderId = "patreon";

    public async Task<DriftDetectionResult> DetectDrift()
    {
        try
        {
            Log.Information("Starting Patreon drift detection");

            // Fetch current state from Patreon API
            var patreonMembers = await _patreonApiClient.GetAllCampaignMembers();

            // Fetch our internal state
            var internalAssignments = await GetActivePatreonAssignments();

            // Analyze the drift
            var result = await AnalyzeDrift(patreonMembers, internalAssignments);

            // Log the results
            LogDriftResults(result);

            Log.Information("Patreon drift detection completed. Found {MissingCount} missing members, {ExtraCount} extra assignments, {MismatchedCount} mismatched tiers",
                result.MissingMembers.Count, result.ExtraAssignments.Count, result.MismatchedTiers.Count);

            return result;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during Patreon drift detection");
            throw;
        }
    }

    private async Task<List<RewardAssignment>> GetActivePatreonAssignments()
    {
        // Get all active assignments for Patreon provider
        var assignments = await _assignmentRepository.GetActiveAssignmentsByProvider(ProviderId);
        return assignments;
    }

    private async Task<DriftDetectionResult> AnalyzeDrift(List<PatreonMember> patreonMembers, List<RewardAssignment> internalAssignments)
    {
        var result = new DriftDetectionResult
        {
            Timestamp = DateTime.UtcNow,
            ProviderId = ProviderId
        };

        // Create lookup dictionaries for efficient comparison
        // First, resolve Patreon user IDs to BattleTags for all members
        var patreonByBattleTag = new Dictionary<string, PatreonMember>();
        foreach (var member in patreonMembers.Where(m => !string.IsNullOrEmpty(m.PatreonUserId)))
        {
            var accountLink = await _patreonLinkRepository.GetByPatreonUserId(member.PatreonUserId);
            if (accountLink != null)
            {
                patreonByBattleTag[accountLink.BattleTag.ToLowerInvariant()] = member;
            }
        }

        var internalByUserId = internalAssignments
            .GroupBy(a => a.UserId.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.ToList());

        // Find missing members (in Patreon but not in our system)
        foreach (var kvp in patreonByBattleTag)
        {
            var battleTag = kvp.Key;
            var patreonMember = kvp.Value;

            if (!patreonMember.IsActivePatron)
            {
                continue; // Skip non-active patrons
            }

            if (!internalByUserId.ContainsKey(battleTag))
            {
                result.MissingMembers.Add(new MissingMember
                {
                    PatreonMemberId = patreonMember.Id,
                    PatreonUserId = patreonMember.PatreonUserId,
                    PatronStatus = patreonMember.PatronStatus,
                    EntitledTierIds = patreonMember.EntitledTierIds,
                    Reason = "Active patron found in Patreon but no active rewards in our system"
                });
            }
            else
            {
                // Check if tiers match
                var assignments = internalByUserId[battleTag];
                var internalTierIds = ExtractTierIdsFromAssignments(assignments);

                if (!AreTierSetsEqual(patreonMember.EntitledTierIds, internalTierIds))
                {
                    result.MismatchedTiers.Add(new TierMismatch
                    {
                        UserId = battleTag,
                        PatreonMemberId = patreonMember.Id,
                        PatreonTiers = patreonMember.EntitledTierIds,
                        InternalTiers = internalTierIds,
                        Reason = "Tier entitlements don't match between Patreon and internal state"
                    });
                }
            }
        }

        // Find extra assignments (in our system but not active in Patreon)
        foreach (var kvp in internalByUserId)
        {
            var battleTag = kvp.Key;
            var assignments = kvp.Value;

            if (!patreonByBattleTag.ContainsKey(battleTag))
            {
                // No matching Patreon member found
                result.ExtraAssignments.AddRange(assignments.Select(a => new ExtraAssignment
                {
                    AssignmentId = a.Id,
                    UserId = a.UserId,
                    RewardId = a.RewardId,
                    AssignedAt = a.AssignedAt,
                    Reason = "Active reward assignment but no corresponding active Patreon member"
                }));
            }
            else
            {
                var patreonMember = patreonByBattleTag[battleTag];
                if (!patreonMember.IsActivePatron)
                {
                    // Patreon member exists but is not active
                    result.ExtraAssignments.AddRange(assignments.Select(a => new ExtraAssignment
                    {
                        AssignmentId = a.Id,
                        UserId = a.UserId,
                        RewardId = a.RewardId,
                        AssignedAt = a.AssignedAt,
                        PatreonStatus = patreonMember.PatronStatus,
                        Reason = $"Patreon member is not active (status: {patreonMember.PatronStatus}, charge: {patreonMember.LastChargeStatus})"
                    }));
                }
            }
        }

        // Calculate summary statistics
        result.TotalPatreonMembers = patreonMembers.Count;
        result.ActivePatreonMembers = patreonMembers.Count(m => m.IsActivePatron);
        result.TotalInternalAssignments = internalAssignments.Count;
        result.UniqueInternalUsers = internalByUserId.Count;
        
        // Log how many Patreon members have linked accounts
        var linkedActiveMembers = patreonByBattleTag.Count(kvp => kvp.Value.IsActivePatron);
        var totalActiveMembers = patreonMembers.Count(m => m.IsActivePatron);
        Log.Information("Patreon drift analysis: {LinkedActive}/{TotalActive} active patrons have linked BattleTags", 
            linkedActiveMembers, totalActiveMembers);

        return result;
    }

    private List<string> ExtractTierIdsFromAssignments(List<RewardAssignment> assignments)
    {
        var tierIds = new HashSet<string>();

        foreach (var assignment in assignments)
        {
            // Extract tier IDs from metadata - this is the ONLY authoritative source
            if (assignment.Metadata != null && assignment.Metadata.TryGetValue("tier_id", out var tierIdObj))
            {
                if (tierIdObj is string tierId && !string.IsNullOrEmpty(tierId))
                {
                    tierIds.Add(tierId);
                }
            }
            else
            {
                // FAIL HARD: Assignment without tier metadata indicates system inconsistency
                throw new InvalidOperationException($"Assignment {assignment.Id} for user {assignment.UserId} has no tier_id metadata. All Patreon assignments must have tier tracking.");
            }
        }

        return tierIds.ToList();
    }

    private bool AreTierSetsEqual(List<string> patreonTiers, List<string> internalTiers)
    {
        if (patreonTiers == null && internalTiers == null) return true;
        if (patreonTiers == null || internalTiers == null) return false;

        var patreonSet = new HashSet<string>(patreonTiers);
        var internalSet = new HashSet<string>(internalTiers);

        return patreonSet.SetEquals(internalSet);
    }

    private void LogDriftResults(DriftDetectionResult result)
    {
        if (result.HasDrift)
        {
            Log.Warning("Patreon drift detected! Missing: {Missing}, Extra: {Extra}, Mismatched: {Mismatched}",
                result.MissingMembers.Count, result.ExtraAssignments.Count, result.MismatchedTiers.Count);

            // Log details for missing members
            foreach (var missing in result.MissingMembers.Take(10)) // Log first 10 to avoid spam
            {
                Log.Warning("Missing member: Email={Email}, PatreonId={Id}, Status={Status}, Tiers={Tiers}",
                    missing.PatreonUserId, missing.PatreonMemberId, missing.PatronStatus,
                    string.Join(",", missing.EntitledTierIds ?? new List<string>()));
            }

            // Log details for extra assignments
            foreach (var extra in result.ExtraAssignments.Take(10))
            {
                Log.Warning("Extra assignment: UserId={UserId}, AssignmentId={Id}, Reason={Reason}",
                    extra.UserId, extra.AssignmentId, extra.Reason);
            }

            // Log tier mismatches
            foreach (var mismatch in result.MismatchedTiers.Take(10))
            {
                Log.Warning("Tier mismatch for user {UserId}: Patreon={PatreonTiers}, Internal={InternalTiers}",
                    mismatch.UserId,
                    string.Join(",", mismatch.PatreonTiers ?? new List<string>()),
                    string.Join(",", mismatch.InternalTiers ?? new List<string>()));
            }

            if (result.MissingMembers.Count > 10 || result.ExtraAssignments.Count > 10 || result.MismatchedTiers.Count > 10)
            {
                Log.Warning("More drift entries exist but were not logged to avoid spam. Total counts: Missing={Missing}, Extra={Extra}, Mismatched={Mismatched}",
                    result.MissingMembers.Count, result.ExtraAssignments.Count, result.MismatchedTiers.Count);
            }
        }
        else
        {
            Log.Information("No Patreon drift detected. System is in sync.");
        }

        // Always log summary statistics
        Log.Information("Patreon drift detection summary: PatreonTotal={PatreonTotal}, PatreonActive={PatreonActive}, InternalAssignments={InternalTotal}, InternalUsers={InternalUsers}",
            result.TotalPatreonMembers, result.ActivePatreonMembers, result.TotalInternalAssignments, result.UniqueInternalUsers);
    }

    public async Task<SyncResult> SyncDrift(DriftDetectionResult driftResult, bool dryRun = false)
    {
        var syncResult = new SyncResult
        {
            SyncTimestamp = DateTime.UtcNow,
            WasDryRun = dryRun
        };

        try
        {
            Log.Information("[DRIFT-SYNC] Starting Patreon drift synchronization. DryRun: {DryRun}, Missing: {Missing}, Extra: {Extra}, Mismatched: {Mismatched}",
                dryRun, driftResult.MissingMembers.Count, driftResult.ExtraAssignments.Count, driftResult.MismatchedTiers.Count);

            if (!driftResult.HasDrift)
            {
                Log.Information("[DRIFT-SYNC] No drift detected, sync not needed");
                syncResult.Success = true;
                return syncResult;
            }

            // Process missing members (need to add rewards)
            foreach (var missingMember in driftResult.MissingMembers)
            {
                try
                {
                    var syncEvent = await CreateMissingMemberSyncEvent(missingMember);
                    if (syncEvent == null)
                    {
                        // Member has no linked BattleTag, skip
                        continue;
                    }
                    
                    syncResult.GeneratedEvents.Add(syncEvent);

                    if (!dryRun)
                    {
                        await _rewardService.ProcessRewardEvent(syncEvent);
                    }

                    syncResult.MembersAdded++;
                    Log.Information("[DRIFT-SYNC] {Action} reward for missing member: {Email} (PatreonId: {Id})",
                        dryRun ? "Would add" : "Added", missingMember.PatreonUserId, missingMember.PatreonMemberId);
                }
                catch (Exception ex)
                {
                    var error = $"Failed to sync missing member {missingMember.PatreonUserId}: {ex.Message}";
                    syncResult.Errors.Add(error);
                    Log.Error(ex, "[DRIFT-SYNC] {Error}", error);
                }
            }

            // Process extra assignments (need to revoke rewards)
            foreach (var extraAssignment in driftResult.ExtraAssignments)
            {
                try
                {
                    var syncEvent = CreateExtraAssignmentSyncEvent(extraAssignment);
                    syncResult.GeneratedEvents.Add(syncEvent);

                    if (!dryRun)
                    {
                        await _rewardService.ProcessRewardEvent(syncEvent);
                    }

                    syncResult.AssignmentsRevoked++;
                    Log.Information("[DRIFT-SYNC] {Action} reward for extra assignment: {UserId} (AssignmentId: {Id})",
                        dryRun ? "Would revoke" : "Revoked", extraAssignment.UserId, extraAssignment.AssignmentId);
                }
                catch (Exception ex)
                {
                    var error = $"Failed to sync extra assignment {extraAssignment.AssignmentId}: {ex.Message}";
                    syncResult.Errors.Add(error);
                    Log.Error(ex, "[DRIFT-SYNC] {Error}", error);
                }
            }

            // Process tier mismatches (need to update tiers)
            foreach (var tierMismatch in driftResult.MismatchedTiers)
            {
                try
                {
                    var syncEvent = CreateTierMismatchSyncEvent(tierMismatch);
                    syncResult.GeneratedEvents.Add(syncEvent);

                    if (!dryRun)
                    {
                        await _rewardService.ProcessRewardEvent(syncEvent);
                    }

                    syncResult.TiersUpdated++;
                    Log.Information("[DRIFT-SYNC] {Action} tiers for user: {UserId} (Patreon: [{PatreonTiers}], Internal: [{InternalTiers}])",
                        dryRun ? "Would update" : "Updated", tierMismatch.UserId,
                        string.Join(",", tierMismatch.PatreonTiers ?? new List<string>()),
                        string.Join(",", tierMismatch.InternalTiers ?? new List<string>()));
                }
                catch (Exception ex)
                {
                    var error = $"Failed to sync tier mismatch for user {tierMismatch.UserId}: {ex.Message}";
                    syncResult.Errors.Add(error);
                    Log.Error(ex, "[DRIFT-SYNC] {Error}", error);
                }
            }

            syncResult.Success = syncResult.Errors.Count == 0;

            Log.Information("[DRIFT-SYNC] Patreon drift synchronization completed. Success: {Success}, DryRun: {DryRun}, Added: {Added}, Revoked: {Revoked}, Updated: {Updated}, Errors: {ErrorCount}",
                syncResult.Success, dryRun, syncResult.MembersAdded, syncResult.AssignmentsRevoked, syncResult.TiersUpdated, syncResult.Errors.Count);

            return syncResult;
        }
        catch (Exception ex)
        {
            syncResult.Success = false;
            syncResult.Errors.Add($"Sync failed with exception: {ex.Message}");
            Log.Error(ex, "[DRIFT-SYNC] Error during Patreon drift synchronization");
            return syncResult;
        }
    }

    private async Task<RewardEvent> CreateMissingMemberSyncEvent(MissingMember missingMember)
    {
        var eventId = $"drift-sync:patreon:{DateTime.UtcNow:yyyy-MM-dd}:missing:{missingMember.PatreonMemberId}";
        
        // Resolve Patreon user ID to BattleTag
        string battleTag = null;
        if (!string.IsNullOrEmpty(missingMember.PatreonUserId))
        {
            var accountLink = await _patreonLinkRepository.GetByPatreonUserId(missingMember.PatreonUserId);
            battleTag = accountLink?.BattleTag;
        }
        
        if (string.IsNullOrEmpty(battleTag))
        {
            Log.Information("Skipping missing member sync for PatreonUserId {PatreonUserId} (MemberId: {MemberId}, Email: {Email}) - no linked BattleTag found", 
                missingMember.PatreonUserId, missingMember.PatreonMemberId);
            return null;
        }
        
        return new RewardEvent
        {
            EventId = eventId,
            EventType = RewardEventType.SubscriptionCreated,
            ProviderId = ProviderId,
            UserId = battleTag,
            ProviderReference = $"sync:member:{missingMember.PatreonMemberId}",
            EntitledTierIds = missingMember.EntitledTierIds ?? new List<string>(),
            Timestamp = DateTime.UtcNow,
            Metadata = new Dictionary<string, object>
            {
                ["event_source"] = "drift_sync",
                ["sync_reason"] = "missing_member",
                ["sync_timestamp"] = DateTime.UtcNow,
                ["patreon_status"] = missingMember.PatronStatus,
                ["patreon_user_id"] = missingMember.PatreonUserId,
                ["patreon_user_id"] = missingMember.PatreonUserId,
                ["original_member_id"] = missingMember.PatreonMemberId,
                ["sync_reason_detail"] = missingMember.Reason
            }
        };
    }

    private RewardEvent CreateExtraAssignmentSyncEvent(ExtraAssignment extraAssignment)
    {
        var eventId = $"drift-sync:patreon:{DateTime.UtcNow:yyyy-MM-dd}:extra:{extraAssignment.AssignmentId}";
        
        return new RewardEvent
        {
            EventId = eventId,
            EventType = RewardEventType.SubscriptionCancelled,
            ProviderId = ProviderId,
            UserId = extraAssignment.UserId,
            ProviderReference = $"sync:revoke:{extraAssignment.AssignmentId}",
            EntitledTierIds = new List<string>(), // No longer entitled to any tiers
            Timestamp = DateTime.UtcNow,
            Metadata = new Dictionary<string, object>
            {
                ["event_source"] = "drift_sync",
                ["sync_reason"] = "extra_assignment",
                ["sync_timestamp"] = DateTime.UtcNow,
                ["original_assignment_id"] = extraAssignment.AssignmentId,
                ["revocation_reason"] = extraAssignment.Reason,
                ["assignment_created_at"] = extraAssignment.AssignedAt,
                ["patreon_status"] = extraAssignment.PatreonStatus ?? "unknown"
            }
        };
    }

    private RewardEvent CreateTierMismatchSyncEvent(TierMismatch tierMismatch)
    {
        var eventId = $"drift-sync:patreon:{DateTime.UtcNow:yyyy-MM-dd}:mismatch:{tierMismatch.UserId}:{Guid.NewGuid().ToString("N")[..8]}";
        
        return new RewardEvent
        {
            EventId = eventId,
            EventType = RewardEventType.SubscriptionCreated,
            ProviderId = ProviderId,
            UserId = tierMismatch.UserId,
            ProviderReference = $"sync:tier-update:{tierMismatch.PatreonMemberId}",
            EntitledTierIds = tierMismatch.PatreonTiers ?? new List<string>(), // Use Patreon as source of truth
            Timestamp = DateTime.UtcNow,
            Metadata = new Dictionary<string, object>
            {
                ["event_source"] = "drift_sync",
                ["sync_reason"] = "tier_mismatch",
                ["sync_timestamp"] = DateTime.UtcNow,
                ["previous_tiers"] = string.Join(",", tierMismatch.InternalTiers ?? new List<string>()),
                ["new_tiers"] = string.Join(",", tierMismatch.PatreonTiers ?? new List<string>()),
                ["patreon_member_id"] = tierMismatch.PatreonMemberId,
                ["mismatch_reason"] = tierMismatch.Reason
            }
        };
    }
}

public class DriftDetectionResult
{
    public DateTime Timestamp { get; set; }
    public string ProviderId { get; set; }
    public List<MissingMember> MissingMembers { get; set; } = new();
    public List<ExtraAssignment> ExtraAssignments { get; set; } = new();
    public List<TierMismatch> MismatchedTiers { get; set; } = new();

    // Summary statistics
    public int TotalPatreonMembers { get; set; }
    public int ActivePatreonMembers { get; set; }
    public int TotalInternalAssignments { get; set; }
    public int UniqueInternalUsers { get; set; }

    public bool HasDrift => MissingMembers.Any() || ExtraAssignments.Any() || MismatchedTiers.Any();
}

public class MissingMember
{
    public string PatreonMemberId { get; set; }
    public string PatreonUserId { get; set; }
    public string PatronStatus { get; set; }
    public List<string> EntitledTierIds { get; set; }
    public string Reason { get; set; }
}

public class ExtraAssignment
{
    public string AssignmentId { get; set; }
    public string UserId { get; set; }
    public string RewardId { get; set; }
    public DateTime AssignedAt { get; set; }
    public string PatreonStatus { get; set; }
    public string Reason { get; set; }
}

public class TierMismatch
{
    public string UserId { get; set; }
    public string PatreonMemberId { get; set; }
    public List<string> PatreonTiers { get; set; }
    public List<string> InternalTiers { get; set; }
    public string Reason { get; set; }
}

public class SyncResult
{
    public DateTime SyncTimestamp { get; set; }
    public bool Success { get; set; }
    public int MembersAdded { get; set; }
    public int AssignmentsRevoked { get; set; }
    public int TiersUpdated { get; set; }
    public List<string> Errors { get; set; } = new();
    public bool WasDryRun { get; set; }
    public List<RewardEvent> GeneratedEvents { get; set; } = new();
}