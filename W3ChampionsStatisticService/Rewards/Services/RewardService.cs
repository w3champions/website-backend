using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using W3C.Domain.Rewards.Abstractions;
using W3C.Domain.Rewards.Entities;
using W3C.Domain.Rewards.Events;
using W3C.Domain.Rewards.Repositories;
using W3C.Domain.Rewards.ValueObjects;
using W3ChampionsStatisticService.Hubs;

namespace W3ChampionsStatisticService.Rewards.Services;

public class RewardService : IRewardService
{
    private readonly IRewardRepository _rewardRepo;
    private readonly IRewardAssignmentRepository _assignmentRepo;
    private readonly IProviderConfigurationRepository _configRepo;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RewardService> _logger;
    private readonly WebsiteBackendHub _hub;

    public RewardService(
        IRewardRepository rewardRepo,
        IRewardAssignmentRepository assignmentRepo,
        IProviderConfigurationRepository configRepo,
        IServiceProvider serviceProvider,
        ILogger<RewardService> logger,
        WebsiteBackendHub hub)
    {
        _rewardRepo = rewardRepo;
        _assignmentRepo = assignmentRepo;
        _configRepo = configRepo;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _hub = hub;
    }

    public async Task<RewardAssignment> ProcessRewardEvent(RewardEvent rewardEvent)
    {
        if (rewardEvent == null)
            throw new ArgumentNullException(nameof(rewardEvent));
            
        if (string.IsNullOrEmpty(rewardEvent.ProviderId))
            throw new ArgumentException("ProviderId cannot be null or empty", nameof(rewardEvent));
            
        if (string.IsNullOrEmpty(rewardEvent.UserId))
            throw new ArgumentException("UserId cannot be null or empty", nameof(rewardEvent));
            
        if (string.IsNullOrEmpty(rewardEvent.ProviderReference))
            throw new ArgumentException("ProviderReference cannot be null or empty", nameof(rewardEvent));
            
        if (rewardEvent.EntitledTierIds == null)
            throw new ArgumentException("EntitledTierIds cannot be null", nameof(rewardEvent));

        try
        {
            // Check for duplicate processing (idempotency)
            var existing = await _assignmentRepo.GetByProviderReference(
                rewardEvent.ProviderId, 
                rewardEvent.ProviderReference);
            
            if (existing.Any())
            {
                _logger.LogInformation("Event already processed: {ProviderId}/{Reference}", 
                    rewardEvent.ProviderId, rewardEvent.ProviderReference);
                return existing.First();
            }

            // Get provider configuration and product mapping
            var config = await _configRepo.GetByProviderId(rewardEvent.ProviderId);
            if (config == null)
            {
                throw new InvalidOperationException($"Provider configuration not found for {rewardEvent.ProviderId}");
            }
            
            if (!config.IsActive)
            {
                throw new InvalidOperationException($"Provider {rewardEvent.ProviderId} is not active");
            }

            // Get previous entitled tiers from the database for diffing
            var previousTiers = await GetPreviousEntitledTiers(rewardEvent.UserId, rewardEvent.ProviderId);
            var currentTiers = rewardEvent.EntitledTierIds ?? new List<string>();
            
            // Calculate tier changes
            var addedTiers = currentTiers.Except(previousTiers).ToList();
            var removedTiers = previousTiers.Except(currentTiers).ToList();
            
            _logger.LogInformation("Tier changes for user {UserId}: Added {AddedCount} tiers, Removed {RemovedCount} tiers", 
                rewardEvent.UserId, addedTiers.Count, removedTiers.Count);

            RewardAssignment lastAssignment = null;

            // Process removed tiers (cancellations/expirations)
            foreach (var tierId in removedTiers)
            {
                var productMapping = config.ProductMappings.FirstOrDefault(m => m.ProviderProductId == tierId);
                if (productMapping == null)
                {
                    throw new InvalidOperationException($"No product mapping found for removed tier {tierId} from provider {rewardEvent.ProviderId}");
                }
                
                lastAssignment = await ProcessTierRemoval(rewardEvent, productMapping);
            }

            // Process added tiers (new subscriptions/purchases)
            foreach (var tierId in addedTiers)
            {
                var productMapping = config.ProductMappings.FirstOrDefault(m => m.ProviderProductId == tierId);
                if (productMapping == null)
                {
                    throw new InvalidOperationException($"No product mapping found for added tier {tierId} from provider {rewardEvent.ProviderId}");
                }
                
                lastAssignment = await ProcessTierAddition(rewardEvent, productMapping);
            }

            // Update stored entitled tiers for future diffing
            await UpdateStoredEntitledTiers(rewardEvent.UserId, rewardEvent.ProviderId, currentTiers);

            return lastAssignment;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing reward event");
            throw;
        }
    }

    private async Task<RewardAssignment> ProcessPurchase(RewardEvent rewardEvent, ProductMapping mapping)
    {
        var reward = await _rewardRepo.GetById(mapping.RewardId);
        if (reward == null || !reward.IsActive)
        {
            _logger.LogWarning("Reward {RewardId} not found or inactive", mapping.RewardId);
            return null;
        }

        var assignment = await AssignReward(
            rewardEvent.UserId, 
            mapping.RewardId, 
            rewardEvent.ProviderId, 
            rewardEvent.ProviderReference);

        // Send announcement if amount is provided and public
        if (rewardEvent.AnnouncementAmount.HasValue)
        {
            await SendDonationAnnouncement(rewardEvent);
        }

        return assignment;
    }

    private async Task<RewardAssignment> ProcessSubscriptionCreated(RewardEvent rewardEvent, ProductMapping mapping)
    {
        var reward = await _rewardRepo.GetById(mapping.RewardId);
        if (reward == null || !reward.IsActive)
        {
            _logger.LogWarning("Reward {RewardId} not found or inactive", mapping.RewardId);
            return null;
        }

        var assignment = await AssignReward(
            rewardEvent.UserId,
            mapping.RewardId,
            rewardEvent.ProviderId,
            rewardEvent.ProviderReference);

        // Send new subscriber announcement
        await SendSubscriberAnnouncement(rewardEvent, "new");

        return assignment;
    }

    private async Task<RewardAssignment> ProcessSubscriptionRenewed(RewardEvent rewardEvent, ProductMapping mapping)
    {
        // Find existing assignment to refresh
        var existingAssignments = await _assignmentRepo.GetByUserIdAndStatus(
            rewardEvent.UserId, 
            RewardStatus.Active);
        
        var assignment = existingAssignments
            .FirstOrDefault(a => a.ProviderId == rewardEvent.ProviderId && a.RewardId == mapping.RewardId);
        
        if (assignment != null)
        {
            var reward = await _rewardRepo.GetById(mapping.RewardId);
            var newExpiration = reward.CalculateExpirationDate(DateTime.UtcNow);
            assignment.Refresh(newExpiration ?? DateTime.UtcNow.AddMonths(1));
            await _assignmentRepo.Update(assignment);
            
            _logger.LogInformation("Refreshed subscription for user {UserId}", rewardEvent.UserId);
        }
        
        return assignment;
    }

    private async Task<RewardAssignment> ProcessSubscriptionCancelled(RewardEvent rewardEvent, ProductMapping mapping)
    {
        var assignments = await _assignmentRepo.GetByUserIdAndStatus(
            rewardEvent.UserId, 
            RewardStatus.Active);
        
        var assignment = assignments
            .FirstOrDefault(a => a.ProviderId == rewardEvent.ProviderId && a.RewardId == mapping.RewardId);
        
        if (assignment != null)
        {
            assignment.Revoke("Subscription cancelled");
            await _assignmentRepo.Update(assignment);
            await RevokeRewardModule(assignment);
            
            _logger.LogInformation("Revoked subscription rewards for user {UserId}", rewardEvent.UserId);
        }
        
        return assignment;
    }

    public async Task<RewardAssignment> AssignReward(string userId, string rewardId, string providerId, string providerReference)
    {
        var reward = await _rewardRepo.GetById(rewardId);
        if (reward == null)
        {
            throw new InvalidOperationException($"Reward {rewardId} not found");
        }

        var assignment = new RewardAssignment
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            RewardId = rewardId,
            ProviderId = providerId,
            ProviderReference = providerReference,
            Status = RewardStatus.Active,
            AssignedAt = DateTime.UtcNow,
            ExpiresAt = reward.CalculateExpirationDate(DateTime.UtcNow)
        };

        await _assignmentRepo.Create(assignment);
        
        // Apply the reward through its module
        await ApplyRewardModule(assignment, reward);
        
        _logger.LogInformation("Assigned reward {RewardId} to user {UserId}", rewardId, userId);
        
        return assignment;
    }

    private async Task ApplyRewardModule(RewardAssignment assignment, Reward reward)
    {
        var modules = _serviceProvider.GetServices<IRewardModule>();
        var module = modules.FirstOrDefault(m => m.ModuleId == reward.ModuleId);
        
        if (module != null)
        {
            var context = new RewardContext
            {
                UserId = assignment.UserId,
                RewardId = assignment.RewardId,
                Parameters = reward.Parameters,
                Assignment = assignment
            };
            
            var result = await module.Apply(context);
            if (!result.Success)
            {
                _logger.LogWarning("Failed to apply reward module {ModuleId}: {Message}", 
                    reward.ModuleId, result.Message);
            }
        }
    }

    private async Task RevokeRewardModule(RewardAssignment assignment)
    {
        var reward = await _rewardRepo.GetById(assignment.RewardId);
        if (reward == null) return;
        
        var modules = _serviceProvider.GetServices<IRewardModule>();
        var module = modules.FirstOrDefault(m => m.ModuleId == reward.ModuleId);
        
        if (module != null)
        {
            var context = new RewardContext
            {
                UserId = assignment.UserId,
                RewardId = assignment.RewardId,
                Parameters = reward.Parameters,
                Assignment = assignment
            };
            
            await module.Revoke(context);
        }
    }

    private async Task SendDonationAnnouncement(RewardEvent rewardEvent)
    {
        // Send announcement through SignalR hub
        var announcement = new
        {
            type = "donation",
            userId = rewardEvent.UserId,
            amount = rewardEvent.AnnouncementAmount,
            currency = rewardEvent.Currency,
            provider = rewardEvent.ProviderId,
            timestamp = rewardEvent.Timestamp
        };
        
        // This would broadcast to all connected clients
        _logger.LogInformation("Donation announcement: {UserId} donated {Amount} {Currency}", 
            rewardEvent.UserId, rewardEvent.AnnouncementAmount, rewardEvent.Currency);
    }

    private async Task SendSubscriberAnnouncement(RewardEvent rewardEvent, string type)
    {
        var announcement = new
        {
            type = "subscription",
            subType = type,
            userId = rewardEvent.UserId,
            provider = rewardEvent.ProviderId,
            timestamp = rewardEvent.Timestamp
        };
        
        _logger.LogInformation("New subscriber announcement: {UserId} via {Provider}", 
            rewardEvent.UserId, rewardEvent.ProviderId);
    }

    public async Task<List<RewardAssignment>> GetUserRewards(string userId)
    {
        return await _assignmentRepo.GetByUserId(userId);
    }

    public async Task RevokeReward(string assignmentId)
    {
        var assignment = await _assignmentRepo.GetById(assignmentId);
        if (assignment != null)
        {
            assignment.Revoke("Manual revocation");
            await _assignmentRepo.Update(assignment);
            await RevokeRewardModule(assignment);
        }
    }

    public async Task ExpireReward(string assignmentId)
    {
        var assignment = await _assignmentRepo.GetById(assignmentId);
        if (assignment != null)
        {
            assignment.Expire();
            await _assignmentRepo.Update(assignment);
            await RevokeRewardModule(assignment);
        }
    }

    public async Task<List<RewardAssignment>> GetExpiredRewards()
    {
        return await _assignmentRepo.GetExpiredAssignments(DateTime.UtcNow);
    }

    public async Task ProcessExpiredRewards()
    {
        var expiredRewards = await GetExpiredRewards();
        foreach (var assignment in expiredRewards.Where(a => a.Status == RewardStatus.Active))
        {
            await ExpireReward(assignment.Id);
        }
    }

    private async Task<List<string>> GetPreviousEntitledTiers(string userId, string providerId)
    {
        // TODO: Implement database lookup for previously stored entitled tiers
        // This should query a table that stores user's entitled tiers per provider
        // For now, return empty list - this means first run will treat all current tiers as "added"
        return new List<string>();
    }

    private async Task UpdateStoredEntitledTiers(string userId, string providerId, List<string> entitledTiers)
    {
        // TODO: Implement database storage of user's current entitled tiers
        // This should store/update a record with userId, providerId, and list of entitled tier IDs
        // This data will be used for future diffing
        _logger.LogInformation("Storing entitled tiers for user {UserId}, provider {ProviderId}: [{TierIds}]", 
            userId, providerId, string.Join(", ", entitledTiers));
    }

    private async Task<RewardAssignment> ProcessTierRemoval(RewardEvent rewardEvent, ProductMapping productMapping)
    {
        if (rewardEvent == null)
            throw new ArgumentNullException(nameof(rewardEvent));
        if (productMapping == null)
            throw new ArgumentNullException(nameof(productMapping));
            
        _logger.LogInformation("Processing tier removal: {TierId} for user {UserId}", 
            productMapping.ProviderProductId, rewardEvent.UserId);
        
        try
        {
            // Cancel/revoke existing assignment for this tier
            return await ProcessSubscriptionCancelled(rewardEvent, productMapping);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process tier removal for {TierId}, user {UserId}", 
                productMapping.ProviderProductId, rewardEvent.UserId);
            throw new InvalidOperationException($"Failed to process tier removal for {productMapping.ProviderProductId}", ex);
        }
    }

    private async Task<RewardAssignment> ProcessTierAddition(RewardEvent rewardEvent, ProductMapping productMapping)
    {
        if (rewardEvent == null)
            throw new ArgumentNullException(nameof(rewardEvent));
        if (productMapping == null)
            throw new ArgumentNullException(nameof(productMapping));
            
        _logger.LogInformation("Processing tier addition: {TierId} for user {UserId}", 
            productMapping.ProviderProductId, rewardEvent.UserId);
        
        try
        {
            // Create new assignment based on event type
            return rewardEvent.EventType switch
            {
                RewardEventType.Purchase => await ProcessPurchase(rewardEvent, productMapping),
                RewardEventType.SubscriptionCreated => await ProcessSubscriptionCreated(rewardEvent, productMapping),
                RewardEventType.SubscriptionRenewed => await ProcessSubscriptionRenewed(rewardEvent, productMapping),
                _ => await ProcessSubscriptionCreated(rewardEvent, productMapping) // Default to creation
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process tier addition for {TierId}, user {UserId}", 
                productMapping.ProviderProductId, rewardEvent.UserId);
            throw new InvalidOperationException($"Failed to process tier addition for {productMapping.ProviderProductId}", ex);
        }
    }
}