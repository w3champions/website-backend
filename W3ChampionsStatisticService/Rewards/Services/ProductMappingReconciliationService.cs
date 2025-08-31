using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using W3C.Domain.Rewards.Abstractions;
using W3C.Domain.Rewards.Entities;
using W3C.Domain.Rewards.Events;
using W3C.Domain.Common.Repositories;
using W3C.Domain.Rewards.Repositories;
using W3C.Domain.Rewards.ValueObjects;

namespace W3ChampionsStatisticService.Rewards.Services;

public class ProductMappingReconciliationService(
    IProductMappingService productMappingService,
    IRewardService rewardService,
    ILogger<ProductMappingReconciliationService> logger) : IProductMappingReconciliationService
{
    private readonly IProductMappingService _productMappingService = productMappingService;
    private readonly IRewardService _rewardService = rewardService;
    private readonly ILogger<ProductMappingReconciliationService> _logger = logger;

    public async Task<ProductMappingReconciliationResult> ReconcileProductMapping(
        string mappingId,
        ProductMapping oldMapping,
        ProductMapping newMapping,
        bool dryRun = false)
    {
        if (newMapping == null)
            throw new ArgumentNullException(nameof(newMapping));

        _logger.LogInformation("Starting product mapping reconciliation for {MappingId}: {ProductName} (DryRun: {DryRun})",
            mappingId, newMapping.ProductName, dryRun);

        // Always start with preview to determine what needs to be done
        var result = await GenerateReconciliationPlan(mappingId, newMapping);
        result.WasDryRun = dryRun;

        if (!result.UserReconciliations.Any())
        {
            _logger.LogInformation("No reconciliation actions needed for mapping {MappingId}", mappingId);
            result.Success = true;
            return result;
        }

        _logger.LogInformation("Reconciliation plan for {MappingId}: {UserCount} users affected, {ActionCount} total actions",
            mappingId, result.TotalUsersAffected, result.UserReconciliations.Sum(u => u.Actions.Count));

        // If not dry run, execute the plan
        if (!dryRun)
        {
            // Reset counts before execution since they were already calculated in preview
            result.RewardsAdded = 0;
            result.RewardsRevoked = 0;
            var eventIdPrefix = $"mapping_reconciliation_{mappingId}_{DateTime.UtcNow:yyyyMMddHHmmss}";
            await ExecuteReconciliationPlan(result, newMapping, eventIdPrefix);
        }

        result.Success = result.Errors.Count == 0;

        _logger.LogInformation("Product mapping reconciliation completed for {MappingId}: Success={Success}, Added={Added}, Revoked={Revoked}, Errors={ErrorCount}",
            mappingId, result.Success, result.RewardsAdded, result.RewardsRevoked, result.Errors.Count);

        return result;
    }

    public async Task<ProductMappingReconciliationResult> PreviewReconciliation(string mappingId)
    {
        var mapping = await _productMappingService.GetProductMappingById(mappingId);
        if (mapping == null)
            throw new InvalidOperationException($"Product mapping {mappingId} not found");

        var result = await GenerateReconciliationPlan(mappingId, mapping);
        result.WasDryRun = true;
        result.Success = true;
        return result;
    }

    /// <summary>
    /// Reconciles rewards for a specific user based on their current associations
    /// </summary>
    public async Task<ProductMappingReconciliationResult> ReconcileUserAssociations(string userId, string eventIdPrefix, bool dryRun = false)
    {
        var result = new ProductMappingReconciliationResult
        {
            ProductMappingId = "user-specific",
            ProductMappingName = $"User reconciliation for {userId}",
            ReconciliationTimestamp = DateTime.UtcNow,
            WasDryRun = dryRun
        };

        try
        {
            _logger.LogInformation("Starting user-specific reconciliation for {UserId} (DryRun: {DryRun})", userId, dryRun);

            // Get all active associations for this user
            var activeAssociations = await _productMappingService.GetUserAssociationsByUserId(userId);

            _logger.LogInformation("Found {ActiveCount} active associations for user {UserId}", activeAssociations.Count, userId);

            foreach (var association in activeAssociations)
            {
                var mapping = await _productMappingService.GetProductMappingById(association.ProductMappingId);
                if (mapping == null)
                {
                    _logger.LogWarning("Product mapping {MappingId} not found for association {AssociationId}",
                        association.ProductMappingId, association.Id);
                    continue;
                }

                var userReconciliation = await PlanUserReconciliation(association, mapping);
                if (userReconciliation.Actions.Any())
                {
                    if (!dryRun)
                    {
                        await ExecuteUserReconciliation(userReconciliation, association, mapping, eventIdPrefix);
                    }

                    result.UserReconciliations.Add(userReconciliation);
                    result.RewardsAdded += userReconciliation.Actions.Count(a => a.Type == ReconciliationActionType.Added);
                    result.RewardsRevoked += userReconciliation.Actions.Count(a => a.Type == ReconciliationActionType.Removed);
                }
            }

            result.TotalUsersAffected = result.UserReconciliations.Any() ? 1 : 0;
            result.Success = result.Errors.Count == 0;

            _logger.LogInformation("User reconciliation completed for {UserId}: Success={Success}, Added={Added}, Revoked={Revoked}",
                userId, result.Success, result.RewardsAdded, result.RewardsRevoked);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during user reconciliation for {UserId}", userId);
            result.Success = false;
            result.Errors.Add($"User reconciliation failed: {ex.Message}");
            return result;
        }
    }

    public async Task<ProductMappingReconciliationResult> ReconcileAllMappings(bool dryRun = false)
    {
        var result = new ProductMappingReconciliationResult
        {
            ProductMappingId = "ALL",
            ProductMappingName = "All Product Mappings",
            ReconciliationTimestamp = DateTime.UtcNow,
            WasDryRun = dryRun
        };

        try
        {
            var allMappings = await _productMappingService.GetAllProductMappings();
            _logger.LogInformation("Starting bulk reconciliation for {MappingCount} product mappings (DryRun: {DryRun})",
                allMappings.Count, dryRun);

            foreach (var mapping in allMappings)
            {
                var mappingResult = await ReconcileProductMapping(mapping.Id, mapping, mapping, dryRun);

                result.UserReconciliations.AddRange(mappingResult.UserReconciliations);
                result.RewardsAdded += mappingResult.RewardsAdded;
                result.RewardsRevoked += mappingResult.RewardsRevoked;
                result.Errors.AddRange(mappingResult.Errors);
            }

            result.TotalUsersAffected = result.UserReconciliations.Count;
            result.Success = result.Errors.Count == 0;

            _logger.LogInformation("Bulk reconciliation completed: Success={Success}, Users={Users}, Added={Added}, Revoked={Revoked}, Errors={ErrorCount}",
                result.Success, result.TotalUsersAffected, result.RewardsAdded, result.RewardsRevoked, result.Errors.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during bulk reconciliation");
            result.Success = false;
            result.Errors.Add($"Bulk reconciliation failed: {ex.Message}");
            return result;
        }
    }

    private async Task<ProductMappingReconciliationResult> GenerateReconciliationPlan(string mappingId, ProductMapping mapping)
    {
        var result = new ProductMappingReconciliationResult
        {
            ProductMappingId = mappingId,
            ProductMappingName = mapping.ProductName,
            ReconciliationTimestamp = DateTime.UtcNow
        };

        try
        {
            var affectedUsers = await GetAffectedUsers(mappingId);

            foreach (var association in affectedUsers)
            {
                var userReconciliation = await PlanUserReconciliation(association, mapping);
                if (userReconciliation.Actions.Any())
                {
                    result.UserReconciliations.Add(userReconciliation);

                    // Calculate totals for preview mode
                    result.RewardsAdded += userReconciliation.Actions.Count(a => a.Type == ReconciliationActionType.Added);
                    result.RewardsRevoked += userReconciliation.Actions.Count(a => a.Type == ReconciliationActionType.Removed);
                }
            }

            // Set total users affected to only those who actually need reconciliation
            result.TotalUsersAffected = result.UserReconciliations.Count;

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating reconciliation plan for {MappingId}", mappingId);
            result.Success = false;
            result.Errors.Add($"Plan generation failed: {ex.Message}");
            return result;
        }
    }

    private async Task<UserReconciliationEntry> PlanUserReconciliation(
        ProductMappingUserAssociation association,
        ProductMapping mapping)
    {
        var userReconciliation = new UserReconciliationEntry
        {
            UserId = association.UserId,
            ProductMappingId = association.ProductMappingId,
            ProductMappingName = mapping.ProductName,
            Success = true
        };

        try
        {
            var expectedRewards = mapping.RewardIds ?? new List<string>();
            var currentAssignments = await GetUserAssignmentsForMapping(association.UserId, association.ProductMappingId);
            var currentRewards = currentAssignments.Select(a => a.RewardId).ToList();

            var missingRewards = expectedRewards.Except(currentRewards).ToList();
            var extraRewards = currentRewards.Except(expectedRewards).ToList();

            // Plan reward additions
            foreach (var rewardId in missingRewards)
            {
                userReconciliation.Actions.Add(new ReconciliationAction
                {
                    RewardId = rewardId,
                    Type = ReconciliationActionType.Added,
                    Success = true
                });
            }

            // Plan reward removals
            foreach (var rewardId in extraRewards)
            {
                var assignment = currentAssignments.FirstOrDefault(a => a.RewardId == rewardId);
                userReconciliation.Actions.Add(new ReconciliationAction
                {
                    RewardId = rewardId,
                    Type = ReconciliationActionType.Removed,
                    Success = true,
                    AssignmentId = assignment?.Id
                });
            }
        }
        catch (Exception ex)
        {
            userReconciliation.Success = false;
            userReconciliation.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Failed to plan reconciliation for user {UserId}", association.UserId);
        }

        return userReconciliation;
    }

    private async Task ExecuteReconciliationPlan(ProductMappingReconciliationResult result, ProductMapping mapping, string eventIdPrefix)
    {
        foreach (var userReconciliation in result.UserReconciliations)
        {
            try
            {
                var association = await GetUserAssociation(userReconciliation.UserId, userReconciliation.ProductMappingId);
                if (association == null)
                {
                    userReconciliation.Success = false;
                    userReconciliation.ErrorMessage = "User association not found";
                    result.Errors.Add($"User {userReconciliation.UserId}: Association not found");
                    continue;
                }

                await ExecuteUserReconciliation(userReconciliation, association, mapping, eventIdPrefix);

                if (userReconciliation.Success)
                {
                    result.RewardsAdded += userReconciliation.Actions.Count(a => a.Type == ReconciliationActionType.Added && a.Success);
                    result.RewardsRevoked += userReconciliation.Actions.Count(a => a.Type == ReconciliationActionType.Removed && a.Success);
                }
                else if (!string.IsNullOrEmpty(userReconciliation.ErrorMessage))
                {
                    result.Errors.Add($"User {userReconciliation.UserId}: {userReconciliation.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                userReconciliation.Success = false;
                userReconciliation.ErrorMessage = ex.Message;
                result.Errors.Add($"User {userReconciliation.UserId}: {ex.Message}");
                _logger.LogError(ex, "Failed to execute reconciliation for user {UserId}", userReconciliation.UserId);
            }
        }
    }

    private async Task ExecuteUserReconciliation(
        UserReconciliationEntry userReconciliation,
        ProductMappingUserAssociation association,
        ProductMapping mapping,
        string eventIdPrefix)
    {
        foreach (var action in userReconciliation.Actions)
        {
            try
            {
                if (action.Type == ReconciliationActionType.Added)
                {
                    await AddRewardToUser(association, mapping, action.RewardId, eventIdPrefix);
                }
                else if (action.Type == ReconciliationActionType.Removed)
                {
                    var assignmentId = await RemoveRewardFromUser(association, mapping, action.RewardId);
                    action.AssignmentId = assignmentId;
                }

                action.Success = true;
            }
            catch (Exception ex)
            {
                action.Success = false;
                action.ErrorMessage = ex.Message;
                _logger.LogError(ex, "Failed to execute {ActionType} for reward {RewardId} and user {UserId}",
                    action.Type, action.RewardId, association.UserId);
            }
        }

        userReconciliation.Success = userReconciliation.Actions.All(a => a.Success);
        if (!userReconciliation.Success)
        {
            userReconciliation.ErrorMessage = "One or more reconciliation actions failed";
        }
    }

    private async Task<List<ProductMappingUserAssociation>> GetAffectedUsers(string mappingId)
    {
        return await _productMappingService.GetAssociationsByProductMappingId(mappingId);
    }

    private async Task<ProductMappingUserAssociation> GetUserAssociation(string userId, string mappingId)
    {
        return await _productMappingService.GetUserAssociation(userId, mappingId);
    }

    private async Task<List<RewardAssignment>> GetUserAssignmentsForMapping(string userId, string mappingId)
    {
        var userAssignments = await _rewardService.GetUserRewards(userId);
        var activeAssignments = userAssignments.Where(a => a.Status == RewardStatus.Active).ToList();

        // Get the association to find the provider and tier info for tier-based lookups
        var association = await GetUserAssociation(userId, mappingId);

        // Filter to assignments that came from this product mapping via either:
        // 1. Direct assignment with product_mapping_id metadata (reconciliation system - legacy)
        // 2. Tier-based assignment with matching provider and tier_id (webhook system)
        // 3. ProviderReference matching "reconciliation:{mappingId}" pattern (current reconciliation system)
        var matchingAssignments = activeAssignments.Where(a =>
        {
            // Check for reconciliation ProviderReference pattern (current reconciliation system)
            if (!string.IsNullOrEmpty(a.ProviderReference) && 
                a.ProviderReference == $"reconciliation:{mappingId}")
            {
                return true;
            }

            if (a.Metadata == null) 
            {
                return false;
            }

            // Check for direct product mapping assignment (reconciliation system - legacy)
            if (a.Metadata.ContainsKey("product_mapping_id") &&
                a.Metadata["product_mapping_id"].ToString() == mappingId)
            {
                return true;
            }

            // Check for tier-based assignment from webhook system
            // This handles cases where users got rewards via Patreon/Ko-Fi webhooks
            if (association != null &&
                a.ProviderId == association.ProviderId &&
                a.Metadata.ContainsKey("tier_id") &&
                a.Metadata["tier_id"].ToString() == association.ProviderProductId)
            {
                return true;
            }

            return false;
        }).ToList();

        return matchingAssignments;
    }

    private async Task AddRewardToUser(ProductMappingUserAssociation association, ProductMapping mapping, string rewardId, string eventIdPrefix)
    {
        // Use RewardService.AssignReward() for proper abstraction and module application
        // The provider reference includes the mapping ID for tracking purposes
        var providerReference = $"reconciliation:{mapping.Id}";

        // Generate unique eventId using prefix and reward-specific suffix
        var eventId = $"{eventIdPrefix}_{DateTime.UtcNow:yyyyMMddHHmmssfff}_{rewardId}";

        await _rewardService.AssignRewardWithEventId(
            association.UserId,
            rewardId,
            association.ProviderId,
            providerReference,
            eventId);

        _logger.LogInformation("Added reward {RewardId} to user {UserId} via product mapping reconciliation",
            rewardId, association.UserId);
    }

    private async Task<string> RemoveRewardFromUser(ProductMappingUserAssociation association, ProductMapping mapping, string rewardId)
    {
        // Find the specific assignment to revoke from existing assignments for this mapping
        var userAssignments = await GetUserAssignmentsForMapping(association.UserId, association.ProductMappingId);
        var assignmentToRevoke = userAssignments.FirstOrDefault(a => a.RewardId == rewardId);

        if (assignmentToRevoke == null)
        {
            _logger.LogWarning("No active assignment found for reward {RewardId} and user {UserId} in product mapping {MappingId}",
                rewardId, association.UserId, association.ProductMappingId);
            return null;
        }

        var reason = $"Product mapping reconciliation: Reward removed from {mapping.ProductName}";
        await _rewardService.RevokeReward(assignmentToRevoke.Id, reason);

        _logger.LogInformation("Revoked reward {RewardId} from user {UserId} via product mapping reconciliation (Assignment: {AssignmentId})",
            rewardId, association.UserId, assignmentToRevoke.Id);

        return assignmentToRevoke.Id;
    }
}
