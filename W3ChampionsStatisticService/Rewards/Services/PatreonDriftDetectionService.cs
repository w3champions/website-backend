using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using W3C.Domain.Rewards.Abstractions;
using W3C.Domain.Rewards.Constants;
using W3C.Domain.Rewards.Entities;
using W3C.Domain.Rewards.Events;
using W3C.Domain.Common.Repositories;
using W3C.Domain.Rewards.Repositories;
using W3C.Domain.Rewards.ValueObjects;
using W3ChampionsStatisticService.Rewards.Providers.Patreon;

namespace W3ChampionsStatisticService.Rewards.Services;

public class PatreonDriftDetectionService(
    PatreonApiClient patreonApiClient,
    IProductMappingUserAssociationRepository associationRepository,
    IProductMappingRepository productMappingRepository,
    IPatreonAccountLinkRepository patreonLinkRepository,
    IProductMappingReconciliationService reconciliationService)
{
    private readonly PatreonApiClient _patreonApiClient = patreonApiClient;
    private readonly IProductMappingUserAssociationRepository _associationRepository = associationRepository;
    private readonly IProductMappingRepository _productMappingRepository = productMappingRepository;
    private readonly IPatreonAccountLinkRepository _patreonLinkRepository = patreonLinkRepository;
    private readonly IProductMappingReconciliationService _reconciliationService = reconciliationService;
    private const string ProviderId = "patreon";

    public async Task<DriftDetectionResult> DetectDrift()
    {
        try
        {
            Log.Information("Starting Patreon drift detection");

            // Fetch current state from Patreon API
            var patreonMembers = await _patreonApiClient.GetAllCampaignMembers();

            // Fetch our internal state - now get associations instead of assignments
            var internalAssociations = await GetActivePatreonAssociations();

            // Analyze the drift
            var result = await AnalyzeDrift(patreonMembers, internalAssociations);

            // Log the results
            LogDriftResults(result);

            Log.Information("Patreon drift detection completed. Found {MissingCount} missing members, {ExtraCount} extra associations, {MismatchedCount} mismatched tiers",
                result.MissingMembers.Count, result.ExtraAssignments.Count, result.MismatchedTiers.Count);

            return result;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during Patreon drift detection");
            throw;
        }
    }

    private async Task<List<ProductMappingUserAssociation>> GetActivePatreonAssociations()
    {
        // Get all associations and filter by provider
        var allAssociations = await _associationRepository.GetAll(AssociationStatus.Active);
        var patreonAssociations = allAssociations.Where(a => a.ProviderId == ProviderId);
        return patreonAssociations.ToList();
    }

    private async Task<DriftDetectionResult> AnalyzeDrift(List<PatreonMember> patreonMembers, List<ProductMappingUserAssociation> internalAssociations)
    {
        var result = new DriftDetectionResult
        {
            Timestamp = DateTime.UtcNow,
            ProviderId = ProviderId
        };

        // Get product mappings to apply TIERED filtering
        var allProductMappings = await _productMappingRepository.GetByProviderId(ProviderId);

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

        var internalByUserId = internalAssociations
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
                // Check if tiers match - apply TIERED filtering to Patreon tiers before comparison
                var associations = internalByUserId[battleTag];
                var internalTierIds = ExtractTierIdsFromAssociations(associations);

                // Apply the same TIERED filtering logic that would be applied during sync
                var filteredPatreonTiers = FilterTierIdsForProcessing(patreonMember.EntitledTierIds, allProductMappings);

                if (!AreTierSetsEqual(filteredPatreonTiers, internalTierIds))
                {
                    result.MismatchedTiers.Add(new TierMismatch
                    {
                        UserId = battleTag,
                        PatreonMemberId = patreonMember.Id,
                        PatreonTiers = patreonMember.EntitledTierIds,  // Store original tiers for logging
                        InternalTiers = internalTierIds,
                        Reason = "Tier entitlements don't match between Patreon and internal state (after TIERED filtering)"
                    });
                }
            }
        }

        // Find extra associations (in our system but not active in Patreon)
        foreach (var kvp in internalByUserId)
        {
            var battleTag = kvp.Key;
            var associations = kvp.Value;

            if (!patreonByBattleTag.ContainsKey(battleTag))
            {
                // No matching Patreon member found - mark associations as extra
                result.ExtraAssignments.AddRange(associations.Select(a => new ExtraAssignment
                {
                    AssignmentId = a.Id, // Using association ID
                    UserId = a.UserId,
                    RewardId = null, // Associations don't have specific reward IDs
                    AssignedAt = a.AssignedAt,
                    Reason = "Active product mapping association but no corresponding active Patreon member"
                }));
            }
            else
            {
                var patreonMember = patreonByBattleTag[battleTag];
                if (!patreonMember.IsActivePatron)
                {
                    // Patreon member exists but is not active
                    result.ExtraAssignments.AddRange(associations.Select(a => new ExtraAssignment
                    {
                        AssignmentId = a.Id, // Using association ID
                        UserId = a.UserId,
                        RewardId = null, // Associations don't have specific reward IDs
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
        result.TotalInternalAssignments = internalAssociations.Count; // Using associations now
        result.UniqueInternalUsers = internalByUserId.Count;

        // Log how many Patreon members have linked accounts
        var linkedActiveMembers = patreonByBattleTag.Count(kvp => kvp.Value.IsActivePatron);
        var totalActiveMembers = patreonMembers.Count(m => m.IsActivePatron);
        Log.Information("Patreon drift analysis: {LinkedActive}/{TotalActive} active patrons have linked BattleTags",
            linkedActiveMembers, totalActiveMembers);

        return result;
    }

    private List<string> ExtractTierIdsFromAssociations(List<ProductMappingUserAssociation> associations)
    {
        var tierIds = new HashSet<string>();

        foreach (var association in associations)
        {
            if (!string.IsNullOrEmpty(association.ProviderProductId))
            {
                tierIds.Add(association.ProviderProductId);
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

    /// <summary>
    /// Pre-loads lookup data to avoid N+1 queries during drift sync
    /// </summary>
    private async Task<BatchLookupData> PreLoadBatchLookupData(DriftDetectionResult driftResult)
    {
        // Get all Patreon user IDs from missing members
        var patreonUserIds = driftResult.MissingMembers
            .Where(m => !string.IsNullOrEmpty(m.PatreonUserId))
            .Select(m => m.PatreonUserId)
            .Distinct()
            .ToList();

        // Get all affected user IDs from extra assignments
        var userIds = driftResult.ExtraAssignments
            .Select(e => e.UserId)
            .Distinct()
            .ToList();

        // Batch load account links
        var allAccountLinks = await _patreonLinkRepository.GetAll() ?? new List<PatreonAccountLink>();
        var patreonUserIdToBattleTag = allAccountLinks
            .Where(link => !string.IsNullOrEmpty(link.PatreonUserId) && !string.IsNullOrEmpty(link.BattleTag))
            .ToDictionary(link => link.PatreonUserId, link => link.BattleTag);

        // Batch load all active Patreon associations to avoid individual user lookups
        var allActiveAssociations = await _associationRepository.GetAll(AssociationStatus.Active) ?? new List<ProductMappingUserAssociation>();
        var patreonAssociations = allActiveAssociations.Where(a => a.ProviderId == ProviderId).ToList();
        var userAssociationsLookup = patreonAssociations
            .Where(a => userIds.Contains(a.UserId))
            .GroupBy(a => a.UserId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Batch load all product mappings for Patreon
        var allProductMappings = await _productMappingRepository.GetByProviderId(ProviderId) ?? new List<ProductMapping>();
        // Create dictionary mapping ProductId (tier ID) to ProductMapping for Patreon provider
        var tierIdToProductMapping = allProductMappings
            .SelectMany(pm => pm.ProductProviders
                .Where(pp => pp.ProviderId == ProviderId)
                .Select(pp => new { TierId = pp.ProductId, Mapping = pm }))
            .ToDictionary(x => x.TierId, x => x.Mapping);

        return new BatchLookupData
        {
            PatreonUserIdToBattleTag = patreonUserIdToBattleTag,
            UserAssociationsLookup = userAssociationsLookup,
            TierIdToProductMapping = tierIdToProductMapping
        };
    }

    /// <summary>
    /// Container for pre-loaded lookup data to avoid N+1 queries
    /// </summary>
    private class BatchLookupData
    {
        public Dictionary<string, string> PatreonUserIdToBattleTag { get; set; } = new();
        public Dictionary<string, List<ProductMappingUserAssociation>> UserAssociationsLookup { get; set; } = new();
        public Dictionary<string, ProductMapping> TierIdToProductMapping { get; set; } = new();
    }

    public async Task<SyncResult> SyncDrift(DriftDetectionResult driftResult, bool dryRun = false)
    {
        var syncResult = new SyncResult
        {
            SyncTimestamp = DateTime.UtcNow,
            WasDryRun = dryRun
        };

        // Pre-load data for batch operations to avoid N+1 queries
        var batchLookups = await PreLoadBatchLookupData(driftResult);

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
                    // We no longer create events - we work directly with associations
                    // Check if member has linked BattleTag before processing
                    if (string.IsNullOrEmpty(missingMember.PatreonUserId))
                    {
                        // Member has no linked BattleTag, skip
                        continue;
                    }

                    if (!dryRun)
                    {
                        await CreateOrUpdateUserAssociation(missingMember, batchLookups.PatreonUserIdToBattleTag, dryRun);
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
                    // We no longer create events - we work directly with associations
                    // var syncEvent = CreateExtraAssignmentEvent(extraAssignment);
                    // syncResult.GeneratedEvents.Add(syncEvent);

                    if (!dryRun)
                    {
                        await DeactivateUserAssociation(extraAssignment, batchLookups.UserAssociationsLookup, dryRun);
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
                    // We no longer create events - we work directly with associations
                    // var syncEvent = CreateTierMismatchEvent(tierMismatch);
                    // syncResult.GeneratedEvents.Add(syncEvent);

                    if (!dryRun)
                    {
                        await UpdateUserAssociationTiers(tierMismatch, dryRun);
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

            // Get current associations for this user
            var currentAssociations = await _associationRepository.GetProductMappingsByUserId(battleTag);
            var activePatreonAssociations = currentAssociations.Where(a => a.ProviderId == ProviderId && a.IsActive()).ToList();

            // Determine what sync action is needed
            var syncAction = DetermineRequiredSyncAction(patreonData, activePatreonAssociations);
            result.SyncAction = syncAction;

            if (syncAction == UserSyncAction.None)
            {
                result.Success = true;
                result.Message = "User rewards are already in sync";
                Log.Information("[USER-SYNC] No sync needed for BattleTag {BattleTag} - already in sync", battleTag);
                return result;
            }

            // Process sync action
            if (syncAction != UserSyncAction.None)
            {
                await ProcessUserSyncAction(battleTag, patreonData, activePatreonAssociations, syncAction);
                result.Success = true;
                result.Message = $"Successfully processed {syncAction} for user";

                Log.Information("[USER-SYNC] Successfully processed {SyncAction} for BattleTag {BattleTag} (PatreonUserId: {PatreonUserId})",
                    syncAction, battleTag, patreonUserId);
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
    private UserSyncAction DetermineRequiredSyncAction(PatreonMember patreonData, List<ProductMappingUserAssociation> currentAssociations)
    {
        var internalTierIds = ExtractTierIdsFromAssociations(currentAssociations);
        var patreonTierIds = patreonData.EntitledTierIds ?? new List<string>();

        // If user is not an active patron, they should have no associations
        if (!patreonData.IsActivePatron)
        {
            if (currentAssociations.Any())
            {
                return UserSyncAction.RevokeAll;
            }
            return UserSyncAction.None;
        }

        // User is active patron
        if (!currentAssociations.Any())
        {
            // No internal associations but should have associations
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
    /// Process sync action for a user by creating/updating/deactivating associations
    /// </summary>
    private async Task ProcessUserSyncAction(string battleTag, PatreonMember patreonData, List<ProductMappingUserAssociation> currentAssociations, UserSyncAction syncAction)
    {
        switch (syncAction)
        {
            case UserSyncAction.CreateNew:
                await CreateAssociationsForNewPatron(battleTag, patreonData);
                break;

            case UserSyncAction.UpdateTiers:
                await UpdateAssociationsForTierChange(battleTag, patreonData, currentAssociations);
                break;

            case UserSyncAction.RevokeAll:
                await DeactivateAllUserAssociations(battleTag, currentAssociations);
                break;
        }
    }

    /// <summary>
    /// Creates or updates associations for missing member with batch-optimized lookups
    /// </summary>
    private async Task CreateOrUpdateUserAssociation(MissingMember missingMember, Dictionary<string, string> patreonUserIdToBattleTag, bool dryRun = false)
    {
        // Resolve Patreon user ID to BattleTag using pre-loaded dictionary
        string battleTag = null;
        if (!string.IsNullOrEmpty(missingMember.PatreonUserId))
        {
            patreonUserIdToBattleTag.TryGetValue(missingMember.PatreonUserId, out battleTag);
        }

        if (string.IsNullOrEmpty(battleTag))
        {
            Log.Information("Skipping missing member sync for PatreonUserId {PatreonUserId} (MemberId: {MemberId}) - no linked BattleTag found",
                missingMember.PatreonUserId, missingMember.PatreonMemberId);
            return;
        }

        // Immediately reconcile rewards for this user (unless dry run)
        if (!dryRun)
        {
            // Create associations for each entitled tier using batch-optimized method
            await CreateAssociationsForTiers(battleTag, missingMember.EntitledTierIds ?? new List<string>(), "drift_sync_missing");

            var eventIdPrefix = $"drift_sync_{DateTime.UtcNow:yyyyMMddHHmmss}_{battleTag}";
            var reconciliationResult = await _reconciliationService.ReconcileUserAssociations(battleTag, eventIdPrefix, dryRun: false);
            Log.Information("Reconciled rewards for missing member {PatreonUserId} -> {UserId}: Added={Added}, Revoked={Revoked}",
                missingMember.PatreonUserId, battleTag, reconciliationResult.RewardsAdded, reconciliationResult.RewardsRevoked);
        }
    }

    /// <summary>
    /// Deactivates association for extra assignment using batch-optimized lookups
    /// </summary>
    private async Task DeactivateUserAssociation(ExtraAssignment extraAssignment, Dictionary<string, List<ProductMappingUserAssociation>> userAssociationsLookup, bool dryRun = false)
    {
        // Find and deactivate the association using pre-loaded data
        var associations = userAssociationsLookup.GetValueOrDefault(extraAssignment.UserId) ?? new List<ProductMappingUserAssociation>();
        var activePatreonAssociations = associations.Where(a => a.ProviderId == ProviderId && a.IsActive()).ToList();

        foreach (var association in activePatreonAssociations)
        {
            association.Revoke($"Drift sync: {extraAssignment.Reason}");
            await _associationRepository.Update(association);
        }

        // Immediately reconcile rewards for this user to revoke assignments (unless dry run)
        if (!dryRun)
        {
            var eventIdPrefix = $"extra_assignment_removal_{DateTime.UtcNow:yyyyMMddHHmmss}_{extraAssignment.UserId}";
            var reconciliationResult = await _reconciliationService.ReconcileUserAssociations(extraAssignment.UserId, eventIdPrefix, dryRun: false);
            Log.Information("Reconciled rewards for extra assignment user {UserId}: Added={Added}, Revoked={Revoked}",
                extraAssignment.UserId, reconciliationResult.RewardsAdded, reconciliationResult.RewardsRevoked);
        }
    }

    /// <summary>
    /// Updates associations for tier mismatch
    /// </summary>
    private async Task UpdateUserAssociationTiers(TierMismatch tierMismatch, bool dryRun = false)
    {
        // Deactivate current associations
        var currentAssociations = await _associationRepository.GetProductMappingsByUserId(tierMismatch.UserId);
        var activePatreonAssociations = currentAssociations.Where(a => a.ProviderId == ProviderId && a.IsActive()).ToList();

        foreach (var association in activePatreonAssociations)
        {
            association.Revoke("Tier mismatch - updating to match Patreon");
            await _associationRepository.Update(association);
        }

        // Get all product mappings once to avoid multiple DB calls
        var allProductMappings = await _productMappingRepository.GetByProviderId(ProviderId);

        // Filter tier IDs - for TIERED rewards, only process the first one
        var filteredTierIds = FilterTierIdsForProcessing(tierMismatch.PatreonTiers ?? new List<string>(), allProductMappings);

        // Create new associations for current tiers
        foreach (var tierId in filteredTierIds)
        {
            await CreateOrUpdateAssociation(tierMismatch.UserId, tierId, "drift_sync_tier_update", skipReconciliation: dryRun);
        }

        // Immediately reconcile rewards for this user (unless dry run)
        if (!dryRun)
        {
            var eventIdPrefix = $"tier_mismatch_fix_{DateTime.UtcNow:yyyyMMddHHmmss}_{tierMismatch.UserId}";
            var reconciliationResult = await _reconciliationService.ReconcileUserAssociations(tierMismatch.UserId, eventIdPrefix, dryRun: false);
            Log.Information("Reconciled rewards for tier mismatch user {UserId}: Added={Added}, Revoked={Revoked}",
                tierMismatch.UserId, reconciliationResult.RewardsAdded, reconciliationResult.RewardsRevoked);
        }
    }

    /// <summary>
    /// Filters tier IDs based on ProductMappingType - for TIERED rewards, only process the first tier
    /// </summary>
    private List<string> FilterTierIdsForProcessing(List<string> tierIds, List<ProductMapping> productMappings)
    {
        if (tierIds == null || !tierIds.Any())
            return new List<string>();

        // Group tiers by their mapping type
        var tieredMappings = new List<string>();
        var nonTieredMappings = new List<string>();

        foreach (var tierId in tierIds)
        {
            var mapping = productMappings
                .FirstOrDefault(pm => pm.ProductProviders
                    .Any(pp => pp.ProviderId == ProviderId && pp.ProductId == tierId));

            if (mapping != null && mapping.Type == ProductMappingType.Tiered)
            {
                tieredMappings.Add(tierId);
            }
            else
            {
                nonTieredMappings.Add(tierId);
            }
        }

        // For TIERED mappings, only take the first one
        var resultTiers = new List<string>();
        if (tieredMappings.Any())
        {
            resultTiers.Add(tieredMappings.First());
            if (tieredMappings.Count > 1)
            {
                Log.Debug("Filtering TIERED rewards: keeping only first tier {FirstTier} from {AllTiers}",
                    tieredMappings.First(), string.Join(", ", tieredMappings));
            }
        }

        // Add all non-tiered mappings
        resultTiers.AddRange(nonTieredMappings);

        if (tierIds.Count != resultTiers.Count)
        {
            Log.Information("Filtered tier IDs from [{OriginalTiers}] to [{FilteredTiers}]",
                string.Join(", ", tierIds), string.Join(", ", resultTiers));
        }

        return resultTiers;
    }

    /// <summary>
    /// Creates associations for new patrons
    /// </summary>
    private async Task CreateAssociationsForNewPatron(string battleTag, PatreonMember patreonData)
    {
        // Get all product mappings once to avoid multiple DB calls
        var allProductMappings = await _productMappingRepository.GetByProviderId(ProviderId);

        // Filter tier IDs - for TIERED rewards, only process the first one
        var filteredTierIds = FilterTierIdsForProcessing(patreonData.EntitledTierIds ?? new List<string>(), allProductMappings);

        foreach (var tierId in filteredTierIds)
        {
            // Skip reconciliation per association to avoid duplicate reward assignments
            // We'll do a single reconciliation at the end instead
            await CreateOrUpdateAssociation(battleTag, tierId, "user_sync_new_patron", skipReconciliation: true);
        }

        // Immediately reconcile rewards for this new patron (single reconciliation for all associations)
        var eventIdPrefix = $"account_link_{DateTime.UtcNow:yyyyMMddHHmmss}_{battleTag}";
        var reconciliationResult = await _reconciliationService.ReconcileUserAssociations(battleTag, eventIdPrefix, dryRun: false);
        Log.Information("Reconciled rewards for new patron {UserId}: Added={Added}, Revoked={Revoked}",
            battleTag, reconciliationResult.RewardsAdded, reconciliationResult.RewardsRevoked);
    }

    /// <summary>
    /// Updates associations for tier changes
    /// </summary>
    private async Task UpdateAssociationsForTierChange(string battleTag, PatreonMember patreonData, List<ProductMappingUserAssociation> currentAssociations)
    {
        // Deactivate current associations
        foreach (var association in currentAssociations)
        {
            association.Revoke("Tier change - updating to match Patreon");
            await _associationRepository.Update(association);
        }

        // Get all product mappings once to avoid multiple DB calls
        var allProductMappings = await _productMappingRepository.GetByProviderId(ProviderId);

        // Filter tier IDs - for TIERED rewards, only process the first one
        var filteredTierIds = FilterTierIdsForProcessing(patreonData.EntitledTierIds ?? new List<string>(), allProductMappings);

        // Create new associations
        foreach (var tierId in filteredTierIds)
        {
            await CreateOrUpdateAssociation(battleTag, tierId, "user_sync_tier_update");
        }

        // Immediately reconcile rewards for this user
        var eventIdPrefix = $"tier_update_{DateTime.UtcNow:yyyyMMddHHmmss}_{battleTag}";
        var reconciliationResult = await _reconciliationService.ReconcileUserAssociations(battleTag, eventIdPrefix, dryRun: false);
        Log.Information("Reconciled rewards for tier update user {UserId}: Added={Added}, Revoked={Revoked}",
            battleTag, reconciliationResult.RewardsAdded, reconciliationResult.RewardsRevoked);
    }

    /// <summary>
    /// Deactivates all associations for a user
    /// </summary>
    private async Task DeactivateAllUserAssociations(string battleTag, List<ProductMappingUserAssociation> currentAssociations)
    {
        foreach (var association in currentAssociations)
        {
            association.Revoke("Patron no longer active");
            await _associationRepository.Update(association);
        }

        // Immediately reconcile rewards to revoke all assignments
        var eventIdPrefix = $"deactivate_{DateTime.UtcNow:yyyyMMddHHmmss}_{battleTag}";
        var reconciliationResult = await _reconciliationService.ReconcileUserAssociations(battleTag, eventIdPrefix, dryRun: false);
        Log.Information("Reconciled rewards for deactivated patron {UserId}: Added={Added}, Revoked={Revoked}",
            battleTag, reconciliationResult.RewardsAdded, reconciliationResult.RewardsRevoked);
    }

    /// <summary>
    /// Creates associations for multiple tiers in batch to avoid repeated DB calls
    /// </summary>
    private async Task CreateAssociationsForTiers(string battleTag, List<string> tierIds, string source)
    {
        if (!tierIds.Any()) return;

        // Get all product mappings for these tiers in one call
        var allProductMappings = await _productMappingRepository.GetByProviderId(ProviderId);

        // Filter tier IDs - for TIERED rewards, only process the first one
        var filteredTierIds = FilterTierIdsForProcessing(tierIds, allProductMappings);

        // Filter for mappings that match the tier IDs for Patreon provider
        var tierMappings = allProductMappings
            .SelectMany(pm => pm.ProductProviders
                .Where(pp => pp.ProviderId == ProviderId && filteredTierIds.Contains(pp.ProductId))
                .Select(pp => new { TierId = pp.ProductId, Mapping = pm }))
            .ToDictionary(x => x.TierId, x => x.Mapping);

        // Get existing associations for this user
        var existingAssociations = await _associationRepository.GetProductMappingsByUserId(battleTag);
        var existingMappingIds = new HashSet<string>(existingAssociations
            .Where(a => a.IsActive())
            .Select(a => a.ProductMappingId));

        // Create associations for tiers that don't already exist
        var associationsToCreate = new List<ProductMappingUserAssociation>();

        foreach (var tierId in filteredTierIds)
        {
            if (tierMappings.TryGetValue(tierId, out var productMapping))
            {
                if (!existingMappingIds.Contains(productMapping.Id))
                {
                    var association = new ProductMappingUserAssociation
                    {
                        UserId = battleTag,
                        ProductMappingId = productMapping.Id,
                        ProviderId = ProviderId,
                        ProviderProductId = tierId,
                        AssignedAt = DateTime.UtcNow,
                        Status = AssociationStatus.Active
                    };

                    association.Metadata[MetadataKeys.Source] = source;
                    associationsToCreate.Add(association);
                }
            }
            else
            {
                Log.Warning("No product mapping found for provider {ProviderId} and tier {TierId}", ProviderId, tierId);
            }
        }

        // Batch create all new associations
        foreach (var association in associationsToCreate)
        {
            await _associationRepository.Create(association);
            Log.Information("Created association {AssociationId} for user {UserId} with tier {TierId}",
                association.Id, battleTag, association.ProviderProductId);
        }
    }

    /// <summary>
    /// Creates or updates a single association (kept for backward compatibility)
    /// </summary>
    private async Task CreateOrUpdateAssociation(string battleTag, string tierId, string source, bool skipReconciliation = false)
    {
        // Look for corresponding product mapping
        var productMappings = await _productMappingRepository.GetByProviderAndProductId(ProviderId, tierId);
        var productMapping = productMappings.FirstOrDefault();

        if (productMapping == null)
        {
            Log.Warning("No product mapping found for provider {ProviderId} and tier {TierId}", ProviderId, tierId);
            return;
        }

        // Check if association already exists
        var existingAssociations = await _associationRepository.GetByUserAndProductMapping(battleTag, productMapping.Id);
        var activeAssociation = existingAssociations.FirstOrDefault(a => a.IsActive());

        if (activeAssociation != null)
        {
            Log.Information("Association already exists for user {UserId} and product mapping {MappingId}", battleTag, productMapping.Id);
            return;
        }

        // Create new association
        var association = new ProductMappingUserAssociation
        {
            UserId = battleTag,
            ProductMappingId = productMapping.Id,
            ProviderId = ProviderId,
            ProviderProductId = tierId,
            AssignedAt = DateTime.UtcNow,
            Status = AssociationStatus.Active
        };

        // Add source information to metadata
        association.Metadata["source"] = source;

        await _associationRepository.Create(association);
        Log.Information("Created association for user {UserId} with product mapping {MappingId} (tier: {TierId})",
            battleTag, productMapping.Id, tierId);

        // Immediately reconcile rewards for this user (unless explicitly skipped)
        if (!skipReconciliation)
        {
            var eventIdPrefix = $"association_create_{DateTime.UtcNow:yyyyMMddHHmmss}_{battleTag}";
            var reconciliationResult = await _reconciliationService.ReconcileUserAssociations(battleTag, eventIdPrefix, dryRun: false);
            Log.Information("Reconciled rewards for user {UserId}: Added={Added}, Revoked={Revoked}",
                battleTag, reconciliationResult.RewardsAdded, reconciliationResult.RewardsRevoked);
        }
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
    public List<string> ProcessedAssociations { get; set; } = new();
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
    public List<string> ModifiedAssociations { get; set; } = new();
}

public enum UserSyncAction
{
    None,
    CreateNew,
    UpdateTiers,
    RevokeAll
}
