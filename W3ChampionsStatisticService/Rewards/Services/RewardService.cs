using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using W3C.Domain.Rewards.Abstractions;
using W3C.Domain.Rewards.Entities;
using W3C.Domain.Rewards.Events;
using W3C.Domain.Rewards.Repositories;
using W3C.Domain.Rewards.ValueObjects;
using W3ChampionsStatisticService.Hubs;
using W3ChampionsStatisticService.Rewards;

namespace W3ChampionsStatisticService.Rewards.Services;

public class RewardService : IRewardService
{
    private readonly IRewardRepository _rewardRepo;
    private readonly IRewardAssignmentRepository _assignmentRepo;
    private readonly IProductMappingRepository _productMappingRepo;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RewardService> _logger;
    private readonly IHubContext<WebsiteBackendHub> _hubContext;

    public RewardService(
        IRewardRepository rewardRepo,
        IRewardAssignmentRepository assignmentRepo,
        IProductMappingRepository productMappingRepo,
        IServiceProvider serviceProvider,
        ILogger<RewardService> logger,
        IHubContext<WebsiteBackendHub> hubContext)
    {
        _rewardRepo = rewardRepo;
        _assignmentRepo = assignmentRepo;
        _productMappingRepo = productMappingRepo;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _hubContext = hubContext;
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
            // Check for duplicate processing (idempotency) using EventId only
            // Note: We don't check ProviderReference because users can have multiple tier assignments
            // EventId ensures webhook idempotency while allowing multiple active assignments per user

            // Validate provider is supported and enabled
            if (!ProviderDefinitions.IsProviderSupported(rewardEvent.ProviderId))
            {
                throw new InvalidOperationException($"Provider {rewardEvent.ProviderId} is not supported");
            }
            
            if (!ProviderDefinitions.IsProviderEnabled(rewardEvent.ProviderId))
            {
                throw new InvalidOperationException($"Provider {rewardEvent.ProviderId} is not enabled");
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
                var productMappings = await _productMappingRepo.GetByProviderAndProductId(rewardEvent.ProviderId, tierId);
                if (!productMappings.Any())
                {
                    throw new InvalidOperationException($"No product mapping found for removed tier {tierId} from provider {rewardEvent.ProviderId}");
                }
                
                // Process all mappings that match this provider/product combination
                foreach (var productMapping in productMappings)
                {
                    lastAssignment = await ProcessTierRemoval(rewardEvent, productMapping, tierId);
                }
            }

            // Process added tiers (new subscriptions/purchases)
            foreach (var tierId in addedTiers)
            {
                var productMappings = await _productMappingRepo.GetByProviderAndProductId(rewardEvent.ProviderId, tierId);
                if (!productMappings.Any())
                {
                    throw new InvalidOperationException($"No product mapping found for added tier {tierId} from provider {rewardEvent.ProviderId}");
                }
                
                // Process all mappings that match this provider/product combination
                foreach (var productMapping in productMappings)
                {
                    lastAssignment = await ProcessTierAddition(rewardEvent, productMapping, tierId);
                }
            }

            // Note: No need to store entitled tiers separately - they are derived from active assignments

            return lastAssignment;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing reward event");
            throw;
        }
    }

    private async Task<RewardAssignment> ProcessPurchase(RewardEvent rewardEvent, ProductMapping mapping, string tierId)
    {
        var rewardIds = mapping.RewardIds ?? new List<string>();
        RewardAssignment lastAssignment = null;

        // Check if user already has assignments for this tier (to avoid duplicates)
        var existingAssignments = await _assignmentRepo.GetByUserIdAndStatus(rewardEvent.UserId, RewardStatus.Active);
        var existingTierAssignments = existingAssignments.Where(a => 
            a.ProviderId == rewardEvent.ProviderId && 
            a.Metadata.ContainsKey("tier_id") && 
            a.Metadata["tier_id"].ToString() == tierId).ToList();

        if (existingTierAssignments.Any())
        {
            _logger.LogInformation("User {UserId} already has active assignments for tier {TierId}, skipping purchase processing", 
                rewardEvent.UserId, tierId);
            return existingTierAssignments.First();
        }

        foreach (var rewardId in rewardIds.Where(id => !string.IsNullOrEmpty(id)))
        {
            var reward = await _rewardRepo.GetById(rewardId);
            if (reward == null || !reward.IsActive)
            {
                _logger.LogWarning("Reward {RewardId} not found or inactive", rewardId);
                continue;
            }

            lastAssignment = await AssignReward(
                rewardEvent.UserId, 
                rewardId, 
                rewardEvent.ProviderId, 
                rewardEvent.ProviderReference,
                rewardEvent.EventId,
                tierId);
        }

        // Send announcement if amount is provided and public
        if (rewardEvent.AnnouncementAmount.HasValue)
        {
            await SendDonationAnnouncement(rewardEvent);
        }

        return lastAssignment;
    }

    private async Task<RewardAssignment> ProcessSubscriptionCreated(RewardEvent rewardEvent, ProductMapping mapping, string tierId)
    {
        var rewardIds = mapping.RewardIds ?? new List<string>();
        RewardAssignment lastAssignment = null;

        // Check if user already has active assignments for this tier
        var existingAssignments = await _assignmentRepo.GetByUserIdAndStatus(rewardEvent.UserId, RewardStatus.Active);
        var existingTierAssignments = existingAssignments.Where(a => 
            a.ProviderId == rewardEvent.ProviderId && 
            a.Metadata.ContainsKey("tier_id") && 
            a.Metadata["tier_id"].ToString() == tierId).ToList();

        if (existingTierAssignments.Any())
        {
            _logger.LogInformation("User {UserId} already has active assignments for tier {TierId}, skipping creation", 
                rewardEvent.UserId, tierId);
            return existingTierAssignments.First();
        }

        foreach (var rewardId in rewardIds.Where(id => !string.IsNullOrEmpty(id)))
        {
            var reward = await _rewardRepo.GetById(rewardId);
            if (reward == null || !reward.IsActive)
            {
                _logger.LogWarning("Reward {RewardId} not found or inactive", rewardId);
                continue;
            }

            lastAssignment = await AssignReward(
                rewardEvent.UserId,
                rewardId,
                rewardEvent.ProviderId,
                rewardEvent.ProviderReference,
                rewardEvent.EventId,
                tierId);
        }

        // Send new subscriber announcement
        await SendSubscriberAnnouncement(rewardEvent, "new");

        return lastAssignment;
    }

    private async Task<RewardAssignment> ProcessSubscriptionRenewed(RewardEvent rewardEvent, ProductMapping mapping, string tierId)
    {
        var rewardIds = mapping.RewardIds ?? new List<string>();
        RewardAssignment lastAssignment = null;

        // Find existing assignments for this specific tier to refresh
        var existingAssignments = await _assignmentRepo.GetByUserIdAndStatus(
            rewardEvent.UserId, 
            RewardStatus.Active);
        
        var tierAssignments = existingAssignments.Where(a => 
            a.ProviderId == rewardEvent.ProviderId && 
            a.Metadata.ContainsKey("tier_id") && 
            a.Metadata["tier_id"].ToString() == tierId).ToList();
        
        foreach (var assignment in tierAssignments)
        {
            var reward = await _rewardRepo.GetById(assignment.RewardId);
            if (reward != null)
            {
                var newExpiration = reward.CalculateExpirationDate(DateTime.UtcNow);
                assignment.Refresh(newExpiration ?? DateTime.UtcNow.AddMonths(1));
                await _assignmentRepo.Update(assignment);
                
                _logger.LogInformation("Refreshed tier {TierId} subscription for user {UserId}, reward {RewardId}", 
                    tierId, rewardEvent.UserId, assignment.RewardId);
                lastAssignment = assignment;
            }
        }
        
        return lastAssignment;
    }

    private async Task<RewardAssignment> ProcessSubscriptionCancelled(RewardEvent rewardEvent, ProductMapping mapping, string tierId)
    {
        var rewardIds = mapping.RewardIds ?? new List<string>();
        RewardAssignment lastAssignment = null;

        // Find assignments specifically for this tier
        var assignments = await _assignmentRepo.GetByUserIdAndStatus(
            rewardEvent.UserId, 
            RewardStatus.Active);
        
        var tierAssignments = assignments.Where(a => 
            a.ProviderId == rewardEvent.ProviderId && 
            a.Metadata.ContainsKey("tier_id") && 
            a.Metadata["tier_id"].ToString() == tierId).ToList();
        
        foreach (var assignment in tierAssignments)
        {
            assignment.Revoke($"Tier {tierId} subscription cancelled");
            await _assignmentRepo.Update(assignment);
            await RevokeRewardModule(assignment);
            
            _logger.LogInformation("Revoked tier {TierId} reward {RewardId} for user {UserId}", tierId, assignment.RewardId, rewardEvent.UserId);
            lastAssignment = assignment;
        }
        
        return lastAssignment;
    }

    public async Task<RewardAssignment> AssignReward(string userId, string rewardId, string providerId, string providerReference)
    {
        return await AssignReward(userId, rewardId, providerId, providerReference, null, null);
    }
    
    private async Task<RewardAssignment> AssignReward(string userId, string rewardId, string providerId, string providerReference, string eventId)
    {
        return await AssignReward(userId, rewardId, providerId, providerReference, eventId, null);
    }
    
    private async Task<RewardAssignment> AssignReward(string userId, string rewardId, string providerId, string providerReference, string eventId, string tierId)
    {
        var reward = await _rewardRepo.GetById(rewardId);
        if (reward == null)
        {
            throw new InvalidOperationException($"Reward {rewardId} not found");
        }

        // Generate unique EventId per assignment to avoid duplicate key errors while maintaining webhook idempotency
        // Format: {originalEventId}_{rewardId} - this ensures each reward assignment has unique EventId
        // while still allowing idempotency checks per webhook+reward combination
        var uniqueEventId = !string.IsNullOrEmpty(eventId) ? $"{eventId}_{rewardId}" : null;

        var assignment = new RewardAssignment
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            RewardId = rewardId,
            ProviderId = providerId,
            ProviderReference = providerReference,
            EventId = uniqueEventId, // Unique per reward assignment for proper idempotency
            Status = RewardStatus.Active,
            AssignedAt = DateTime.UtcNow,
            ExpiresAt = reward.CalculateExpirationDate(DateTime.UtcNow),
            Metadata = new Dictionary<string, object>()
        };

        // Store tier ID in metadata if provided
        if (!string.IsNullOrEmpty(tierId))
        {
            assignment.Metadata["tier_id"] = tierId;
        }
        
        // Store original webhook EventId in metadata for audit/tracking purposes
        if (!string.IsNullOrEmpty(eventId))
        {
            assignment.Metadata["original_event_id"] = eventId;
        }

        try
        {
            await _assignmentRepo.Create(assignment);
            
            // Apply the reward through its module
            await ApplyRewardModule(assignment, reward);
            
            _logger.LogInformation("Assigned reward {RewardId} to user {UserId} with EventId {EventId}", rewardId, userId, eventId);
            
            return assignment;
        }
        catch (MongoWriteException ex) when (ex.WriteError.Code == 11000) // Duplicate key error
        {
            _logger.LogInformation("Duplicate EventId detected: {EventId}. Event already processed, returning existing assignment", eventId);
            
            // Find and return the existing assignment
            var existing = await _assignmentRepo.GetByProviderReference(providerId, providerReference);
            return existing.FirstOrDefault() ?? throw new InvalidOperationException("Duplicate key error but no existing assignment found");
        }
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
        var announcement = new
        {
            type = "donation",
            userId = rewardEvent.UserId,
            amount = rewardEvent.AnnouncementAmount,
            currency = rewardEvent.Currency,
            provider = rewardEvent.ProviderId,
            timestamp = rewardEvent.Timestamp
        };
        
        // Broadcast to all connected clients
        if (_hubContext != null)
        {
            await _hubContext.Clients.All.SendAsync("DonationAnnouncement", announcement);
        }
        
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
        
        // Broadcast to all connected clients
        if (_hubContext != null)
        {
            await _hubContext.Clients.All.SendAsync("SubscriberAnnouncement", announcement);
        }
        
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
        // Query existing active assignments for this user and provider
        var activeAssignments = await _assignmentRepo.GetByUserIdAndStatus(userId, RewardStatus.Active);
        var providerAssignments = activeAssignments.Where(a => a.ProviderId == providerId).ToList();
        
        var tierIds = new HashSet<string>();
        
        foreach (var assignment in providerAssignments)
        {
            // Extract tier ID from metadata - this is the authoritative source
            if (assignment.Metadata != null && assignment.Metadata.TryGetValue("tier_id", out var tierIdObj))
            {
                if (tierIdObj is string tierId && !string.IsNullOrEmpty(tierId))
                {
                    tierIds.Add(tierId);
                }
            }
        }
        
        return tierIds.ToList();
    }


    private async Task<RewardAssignment> ProcessTierRemoval(RewardEvent rewardEvent, ProductMapping productMapping, string tierId)
    {
        if (rewardEvent == null)
            throw new ArgumentNullException(nameof(rewardEvent));
        if (productMapping == null)
            throw new ArgumentNullException(nameof(productMapping));
            
        _logger.LogInformation("Processing tier removal: {TierId} for user {UserId}", 
            tierId, rewardEvent.UserId);
        
        try
        {
            // Cancel/revoke existing assignment for this tier
            return await ProcessSubscriptionCancelled(rewardEvent, productMapping, tierId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process tier removal for {TierId}, user {UserId}", 
                tierId, rewardEvent.UserId);
            throw new InvalidOperationException($"Failed to process tier removal for {tierId}", ex);
        }
    }

    private async Task<RewardAssignment> ProcessTierAddition(RewardEvent rewardEvent, ProductMapping productMapping, string tierId)
    {
        if (rewardEvent == null)
            throw new ArgumentNullException(nameof(rewardEvent));
        if (productMapping == null)
            throw new ArgumentNullException(nameof(productMapping));
            
        _logger.LogInformation("Processing tier addition: {TierId} for user {UserId}", 
            tierId, rewardEvent.UserId);
        
        try
        {
            // Create new assignment based on event type
            return rewardEvent.EventType switch
            {
                RewardEventType.Purchase => await ProcessPurchase(rewardEvent, productMapping, tierId),
                RewardEventType.SubscriptionCreated => await ProcessSubscriptionCreated(rewardEvent, productMapping, tierId),
                RewardEventType.SubscriptionRenewed => await ProcessSubscriptionRenewed(rewardEvent, productMapping, tierId),
                _ => await ProcessSubscriptionCreated(rewardEvent, productMapping, tierId) // Default to creation
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process tier addition for {TierId}, user {UserId}", 
                tierId, rewardEvent.UserId);
            throw new InvalidOperationException($"Failed to process tier addition for {tierId}", ex);
        }
    }
}