using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using W3C.Domain.Rewards.Abstractions;
using W3C.Domain.Rewards.Entities;
using W3C.Domain.Rewards.Events;
using W3C.Domain.Rewards.Repositories;
using W3C.Domain.Rewards.ValueObjects;
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
        // First, get all account links in one batch to avoid repeated repository calls
        var allAccountLinks = await _patreonLinkRepository.GetAll();
        var patreonUserIdToAccountLink = allAccountLinks
            .Where(link => !string.IsNullOrEmpty(link.PatreonUserId))
            .ToDictionary(link => link.PatreonUserId, link => link);

        // Now resolve Patreon user IDs to BattleTags using the lookup dictionary
        var patreonByBattleTag = new Dictionary<string, PatreonMember>();
        foreach (var member in patreonMembers.Where(m => !string.IsNullOrEmpty(m.PatreonUserId)))
        {
            if (patreonUserIdToAccountLink.TryGetValue(member.PatreonUserId, out var accountLink))
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
                    var syncEvent = await CreateMissingMemberEvent(missingMember);
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
                    Log.Information("[DRIFT-SYNC] {Action} reward for missing member: {PatreonUserId} (PatreonId: {Id})",
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
                    var syncEvent = CreateExtraAssignmentEvent(extraAssignment);
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
                    var syncEvent = CreateTierMismatchEvent(tierMismatch);
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

    /// <summary>
    /// Sync a single user's rewards using their OAuth access token for fresh data
    /// This is efficient and provides immediate sync when a user links their account
    /// </summary>
    public async Task<UserSyncResult> SyncSingleUser(string battleTag, string patreonUserId, string accessToken = null)
    {
        var result = new UserSyncResult
        {
            BattleTag = battleTag,
            PatreonUserId = patreonUserId,
            SyncTimestamp = DateTime.UtcNow,
            AccessTokenUsed = !string.IsNullOrEmpty(accessToken)
        };

        try
        {
            if (string.IsNullOrEmpty(accessToken))
            {
                result.Success = false;
                result.ErrorMessage = "Access token required for single user sync";
                Log.Warning("Skipping single user sync for {BattleTag} - no access token available", battleTag);
                return result;
            }

            Log.Information("[USER-SYNC] Starting single user sync for BattleTag {BattleTag}", battleTag);

            // Fetch fresh Patreon data using access token
            var patreonData = await _patreonApiClient.GetUserMemberships(accessToken);
            if (patreonData == null)
            {
                result.Success = false;
                result.ErrorMessage = "Failed to fetch user membership data from Patreon";
                Log.Error("[USER-SYNC] Failed to fetch Patreon data for BattleTag {BattleTag}", battleTag);
                return result;
            }

            // Ensure the Patreon user ID matches what we expect
            if (patreonData.PatreonUserId != patreonUserId)
            {
                result.Success = false;
                result.ErrorMessage = $"Patreon user ID mismatch: expected {patreonUserId}, got {patreonData.PatreonUserId}";
                Log.Error("[USER-SYNC] Patreon user ID mismatch for BattleTag {BattleTag}: expected {Expected}, got {Actual}",
                    battleTag, patreonUserId, patreonData.PatreonUserId);
                return result;
            }

            // Get current reward assignments for this user
            var currentAssignments = await _assignmentRepository.GetByUserIdAndStatus(battleTag, RewardStatus.Active);
            var patreonAssignments = currentAssignments.Where(a => a.ProviderId == ProviderId).ToList();

            // Determine what sync action is needed
            var syncAction = DetermineRequiredSyncAction(patreonData, patreonAssignments);
            result.SyncAction = syncAction;

            if (syncAction == UserSyncAction.None)
            {
                result.Success = true;
                result.Message = "User rewards are already in sync";
                Log.Information("[USER-SYNC] No sync needed for BattleTag {BattleTag} - already in sync", battleTag);
                return result;
            }

            // Create and process sync event
            var syncEvent = CreateSyncEventForUserData(battleTag, patreonData, patreonAssignments, syncAction);
            if (syncEvent != null)
            {
                result.GeneratedEvent = syncEvent;
                await _rewardService.ProcessRewardEvent(syncEvent);
                result.Success = true;
                result.Message = $"Successfully processed {syncAction} for user";

                Log.Information("[USER-SYNC] Successfully processed {SyncAction} for BattleTag {BattleTag} (PatreonUserId: {PatreonUserId})",
                    syncAction, battleTag, patreonUserId);
            }
            else
            {
                result.Success = false;
                result.ErrorMessage = "Failed to create sync event";
                Log.Error("[USER-SYNC] Failed to create sync event for BattleTag {BattleTag}", battleTag);
            }

            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = $"Exception during user sync: {ex.Message}";
            Log.Error(ex, "[USER-SYNC] Error during single user sync for BattleTag {BattleTag}", battleTag);
            return result;
        }
    }

    /// <summary>
    /// Determines what sync action is needed for a user based on their Patreon data vs internal state
    /// </summary>
    private UserSyncAction DetermineRequiredSyncAction(PatreonMember patreonData, List<RewardAssignment> currentAssignments)
    {
        var internalTierIds = ExtractTierIdsFromAssignments(currentAssignments);
        var patreonTierIds = patreonData.EntitledTierIds ?? new List<string>();

        // If user is not an active patron, they should have no rewards
        if (!patreonData.IsActivePatron)
        {
            if (currentAssignments.Any())
            {
                return UserSyncAction.RevokeAll;
            }
            return UserSyncAction.None;
        }

        // User is active patron
        if (!currentAssignments.Any())
        {
            // No internal assignments but should have rewards
            return patreonTierIds.Any() ? UserSyncAction.CreateNew : UserSyncAction.None;
        }

        // Compare tier entitlements
        if (!AreTierSetsEqual(patreonTierIds, internalTierIds))
        {
            return UserSyncAction.UpdateTiers;
        }

        return UserSyncAction.None;
    }

    /// <summary>
    /// Creates appropriate RewardEvent for a user based on their sync action needed
    /// </summary>
    private RewardEvent CreateSyncEventForUserData(string battleTag, PatreonMember patreonData, List<RewardAssignment> currentAssignments, UserSyncAction syncAction)
    {
        switch (syncAction)
        {
            case UserSyncAction.CreateNew:
                return CreateRewardEvent(
                    eventSource: "user-sync",
                    syncReason: "new_patron",
                    eventType: RewardEventType.SubscriptionCreated,
                    userId: battleTag,
                    providerReference: $"user-sync:create:{patreonData.Id}",
                    entitledTierIds: patreonData.EntitledTierIds,
                    additionalMetadata: BuildBaseMetadata(
                        "user_sync", 
                        "new_patron", 
                        patreonData.PatreonUserId, 
                        patreonData.PatronStatus, 
                        patreonData.Id)
                );

            case UserSyncAction.UpdateTiers:
                var updateMetadata = BuildBaseMetadata(
                    "user_sync", 
                    "tier_update", 
                    patreonData.PatreonUserId, 
                    patreonData.PatronStatus, 
                    patreonData.Id);
                updateMetadata["previous_tiers"] = string.Join(",", ExtractTierIdsFromAssignments(currentAssignments));
                updateMetadata["new_tiers"] = string.Join(",", patreonData.EntitledTierIds ?? new List<string>());
                
                return CreateRewardEvent(
                    eventSource: "user-sync",
                    syncReason: "tier_update",
                    eventType: RewardEventType.SubscriptionCreated,
                    userId: battleTag,
                    providerReference: $"user-sync:update:{patreonData.Id}",
                    entitledTierIds: patreonData.EntitledTierIds,
                    additionalMetadata: updateMetadata
                );

            case UserSyncAction.RevokeAll:
                var revokeMetadata = BuildBaseMetadata(
                    "user_sync", 
                    "patron_inactive", 
                    patreonData.PatreonUserId, 
                    patreonData.PatronStatus, 
                    patreonData.Id);
                revokeMetadata["last_charge_status"] = patreonData.LastChargeStatus;
                
                return CreateRewardEvent(
                    eventSource: "user-sync",
                    syncReason: "patron_inactive",
                    eventType: RewardEventType.SubscriptionCancelled,
                    userId: battleTag,
                    providerReference: $"user-sync:revoke:{patreonData.Id}",
                    entitledTierIds: new List<string>(),
                    additionalMetadata: revokeMetadata
                );

            case UserSyncAction.None:
            default:
                return null;
        }
    }

    /// <summary>
    /// Creates a RewardEvent with standardized structure and metadata
    /// </summary>
    private RewardEvent CreateRewardEvent(
        string eventSource,
        string syncReason,
        RewardEventType eventType,
        string userId,
        string providerReference,
        List<string> entitledTierIds,
        Dictionary<string, object> additionalMetadata = null)
    {
        var eventId = $"{eventSource}:patreon:{DateTime.UtcNow:yyyy-MM-dd}:{userId}:{Guid.NewGuid().ToString("N")[..8]}";
        
        var metadata = BuildBaseMetadata(eventSource, syncReason);
        
        // Add any additional metadata
        if (additionalMetadata != null)
        {
            foreach (var kvp in additionalMetadata)
            {
                metadata[kvp.Key] = kvp.Value;
            }
        }

        return new RewardEvent
        {
            EventId = eventId,
            EventType = eventType,
            ProviderId = ProviderId,
            UserId = userId,
            ProviderReference = providerReference,
            EntitledTierIds = entitledTierIds ?? new List<string>(),
            Timestamp = DateTime.UtcNow,
            Metadata = metadata
        };
    }

    /// <summary>
    /// Creates base metadata dictionary with common fields
    /// </summary>
    private Dictionary<string, object> BuildBaseMetadata(
        string eventSource,
        string syncReason,
        string patreonUserId = null,
        string patreonStatus = null,
        string patreonMemberId = null)
    {
        var metadata = new Dictionary<string, object>
        {
            ["event_source"] = eventSource,
            ["sync_reason"] = syncReason,
            ["sync_timestamp"] = DateTime.UtcNow
        };

        if (!string.IsNullOrEmpty(patreonUserId))
            metadata["patreon_user_id"] = patreonUserId;
        
        if (!string.IsNullOrEmpty(patreonStatus))
            metadata["patreon_status"] = patreonStatus;
            
        if (!string.IsNullOrEmpty(patreonMemberId))
            metadata["patreon_member_id"] = patreonMemberId;

        return metadata;
    }

    /// <summary>
    /// Creates RewardEvent for missing member using unified event creation
    /// </summary>
    private async Task<RewardEvent> CreateMissingMemberEvent(MissingMember missingMember)
    {
        // Resolve Patreon user ID to BattleTag
        string battleTag = null;
        if (!string.IsNullOrEmpty(missingMember.PatreonUserId))
        {
            var accountLink = await _patreonLinkRepository.GetByPatreonUserId(missingMember.PatreonUserId);
            battleTag = accountLink?.BattleTag;
        }
        
        if (string.IsNullOrEmpty(battleTag))
        {
            Log.Information("Skipping missing member sync for PatreonUserId {PatreonUserId} (MemberId: {MemberId}) - no linked BattleTag found", 
                missingMember.PatreonUserId, missingMember.PatreonMemberId);
            return null;
        }

        var additionalMetadata = BuildBaseMetadata(
            "drift_sync", 
            "missing_member", 
            missingMember.PatreonUserId, 
            missingMember.PatronStatus, 
            missingMember.PatreonMemberId);
        additionalMetadata["sync_reason_detail"] = missingMember.Reason;

        return CreateRewardEvent(
            eventSource: "drift-sync",
            syncReason: "missing_member",
            eventType: RewardEventType.SubscriptionCreated,
            userId: battleTag,
            providerReference: $"sync:member:{missingMember.PatreonMemberId}",
            entitledTierIds: missingMember.EntitledTierIds,
            additionalMetadata: additionalMetadata
        );
    }

    /// <summary>
    /// Creates RewardEvent for extra assignment using unified event creation
    /// </summary>
    private RewardEvent CreateExtraAssignmentEvent(ExtraAssignment extraAssignment)
    {
        var additionalMetadata = BuildBaseMetadata(
            "drift_sync", 
            "extra_assignment", 
            patreonStatus: extraAssignment.PatreonStatus ?? "unknown");
        additionalMetadata["original_assignment_id"] = extraAssignment.AssignmentId;
        additionalMetadata["revocation_reason"] = extraAssignment.Reason;
        additionalMetadata["assignment_created_at"] = extraAssignment.AssignedAt;

        return CreateRewardEvent(
            eventSource: "drift-sync",
            syncReason: "extra_assignment",
            eventType: RewardEventType.SubscriptionCancelled,
            userId: extraAssignment.UserId,
            providerReference: $"sync:revoke:{extraAssignment.AssignmentId}",
            entitledTierIds: new List<string>(),
            additionalMetadata: additionalMetadata
        );
    }

    /// <summary>
    /// Creates RewardEvent for tier mismatch using unified event creation
    /// </summary>
    private RewardEvent CreateTierMismatchEvent(TierMismatch tierMismatch)
    {
        var additionalMetadata = BuildBaseMetadata(
            "drift_sync", 
            "tier_mismatch", 
            patreonMemberId: tierMismatch.PatreonMemberId);
        additionalMetadata["previous_tiers"] = string.Join(",", tierMismatch.InternalTiers ?? new List<string>());
        additionalMetadata["new_tiers"] = string.Join(",", tierMismatch.PatreonTiers ?? new List<string>());
        additionalMetadata["mismatch_reason"] = tierMismatch.Reason;

        return CreateRewardEvent(
            eventSource: "drift-sync",
            syncReason: "tier_mismatch",
            eventType: RewardEventType.SubscriptionCreated,
            userId: tierMismatch.UserId,
            providerReference: $"sync:tier-update:{tierMismatch.PatreonMemberId}",
            entitledTierIds: tierMismatch.PatreonTiers,
            additionalMetadata: additionalMetadata
        );
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

public class UserSyncResult
{
    public DateTime SyncTimestamp { get; set; }
    public bool Success { get; set; }
    public string BattleTag { get; set; }
    public string PatreonUserId { get; set; }
    public bool AccessTokenUsed { get; set; }
    public UserSyncAction SyncAction { get; set; }
    public string Message { get; set; }
    public string ErrorMessage { get; set; }
    public RewardEvent GeneratedEvent { get; set; }
}

public enum UserSyncAction
{
    None,
    CreateNew,
    UpdateTiers,
    RevokeAll
}