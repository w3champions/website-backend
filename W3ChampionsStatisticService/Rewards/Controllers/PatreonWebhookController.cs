using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Serilog;
using W3C.Domain.Rewards.Abstractions;
using W3C.Domain.Rewards.Entities;
using W3C.Domain.Rewards.Events;
using W3C.Domain.Rewards.Repositories;
using W3ChampionsStatisticService.Rewards.Providers.Patreon;
using W3ChampionsStatisticService.Rewards.Services;

namespace W3ChampionsStatisticService.Rewards.Controllers;

[ApiController]
[Route("api/rewards/webhooks/patreon")]
public class PatreonWebhookController(
    PatreonProvider patreonProvider,
    IProductMappingUserAssociationRepository associationRepository,
    IProductMappingRepository productMappingRepository,
    IProductMappingReconciliationService reconciliationService,
    IPatreonAccountLinkRepository patreonAccountLinkRepository,
    ILogger<PatreonWebhookController> logger) : ControllerBase
{
    private readonly PatreonProvider _patreonProvider = patreonProvider;
    private readonly IProductMappingUserAssociationRepository _associationRepository = associationRepository;
    private readonly IProductMappingRepository _productMappingRepository = productMappingRepository;
    private readonly IProductMappingReconciliationService _reconciliationService = reconciliationService;
    private readonly IPatreonAccountLinkRepository _patreonAccountLinkRepository = patreonAccountLinkRepository;
    private readonly ILogger<PatreonWebhookController> _logger = logger;

    [HttpPost]
    public async Task<IActionResult> HandlePatreonWebhook()
    {
        try
        {
            // Read raw body for signature validation
            using var reader = new StreamReader(Request.Body, Encoding.UTF8);
            var payload = await reader.ReadToEndAsync();

            // Patreon sends signature in X-Patreon-Signature header
            var signature = Request.Headers["X-Patreon-Signature"].FirstOrDefault();

            if (string.IsNullOrEmpty(signature))
            {
                _logger.LogWarning("Patreon webhook received without signature");
                return Unauthorized("Missing signature");
            }

            // Validate webhook signature
            if (!await _patreonProvider.ValidateWebhookSignature(payload, signature, null))
            {
                _logger.LogWarning("Invalid Patreon webhook signature");
                return Unauthorized("Invalid signature");
            }

            // Extract headers for event type determination
            var headers = Request.Headers.ToDictionary(h => h.Key, h => h.Value.FirstOrDefault());

            // Parse webhook event
            var rewardEvent = await _patreonProvider.ParseWebhookEvent(payload, headers);

            // Create/update associations based on the event
            var associationResults = await ProcessPatreonWebhookAssociations(rewardEvent);

            // Immediately trigger user-specific reconciliation to create/update reward assignments
            var reconciliationResult = await _reconciliationService.ReconcileUserAssociations(rewardEvent.UserId, rewardEvent.EventId, dryRun: false);

            // Refresh LastSyncAt on the account link — non-fatal bookkeeping
            try
            {
                var link = await _patreonAccountLinkRepository.GetByBattleTag(rewardEvent.UserId);
                if (link != null)
                {
                    link.UpdateLastSync();
                    await _patreonAccountLinkRepository.Update(link);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to update LastSyncAt for {BattleTag} after webhook", rewardEvent.UserId);
            }

            _logger.LogInformation("Successfully processed Patreon webhook for user {UserId} with {TierCount} entitled tiers. Associations: {AssociationCount}, Rewards Added: {Added}, Rewards Revoked: {Revoked}",
                rewardEvent.UserId, rewardEvent.EntitledTiers.Count, associationResults.Count, reconciliationResult.RewardsAdded, reconciliationResult.RewardsRevoked);

            return Ok(new
            {
                success = true,
                associationsProcessed = associationResults.Count,
                rewardsAdded = reconciliationResult.RewardsAdded,
                rewardsRevoked = reconciliationResult.RewardsRevoked,
                entitledTiers = rewardEvent.EntitledTiers
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Patreon webhook");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Process Patreon webhook by creating/updating product mapping user associations
    /// </summary>
    private async Task<List<ProductMappingUserAssociation>> ProcessPatreonWebhookAssociations(RewardEvent rewardEvent)
    {
        var processedAssociations = new List<ProductMappingUserAssociation>();

        try
        {
            // First, revoke any existing active Patreon associations for this user
            var existingAssociations = await _associationRepository.GetProductMappingsByUserId(rewardEvent.UserId);
            var activePatreonAssociations = existingAssociations
                .Where(a => a.ProviderId == rewardEvent.ProviderId && a.IsActive())
                .ToList();

            foreach (var association in activePatreonAssociations)
            {
                association.Revoke($"Patreon webhook update: {rewardEvent.EventType}");
                await _associationRepository.Update(association);
                _logger.LogInformation("Revoked existing association {AssociationId} for user {UserId}",
                    association.Id, rewardEvent.UserId);
            }

            // Create new associations for entitled tiers (if any), applying tier filter
            if (rewardEvent.EntitledTiers?.Any() == true)
            {
                var productMappings = await _productMappingRepository.GetByProviderId(rewardEvent.ProviderId);
                var filteredTierIds = PatreonTierFilter.Filter(rewardEvent.EntitledTiers, productMappings);
                foreach (var tierId in filteredTierIds)
                {
                    var association = await CreateAssociationForTier(rewardEvent, tierId);
                    if (association != null)
                    {
                        processedAssociations.Add(association);
                    }
                }
            }

            return processedAssociations;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Patreon webhook associations for user {UserId}", rewardEvent.UserId);
            throw;
        }
    }

    /// <summary>
    /// Creates a product mapping user association for a specific tier
    /// </summary>
    private async Task<ProductMappingUserAssociation> CreateAssociationForTier(RewardEvent rewardEvent, string tierId)
    {
        try
        {
            // Find product mappings for this provider and tier
            var productMappings = await _productMappingRepository.GetByProviderAndProductId(rewardEvent.ProviderId, tierId);
            var productMapping = productMappings.FirstOrDefault();

            if (productMapping == null)
            {
                _logger.LogWarning("No product mapping found for provider {ProviderId} and tier {TierId} - skipping association creation",
                    rewardEvent.ProviderId, tierId);
                return null;
            }

            // Check if association already exists and is active
            var existingAssociations = await _associationRepository.GetByUserAndProductMapping(rewardEvent.UserId, productMapping.Id);
            var activeAssociation = existingAssociations.FirstOrDefault(a => a.IsActive());

            if (activeAssociation != null)
            {
                _logger.LogInformation("Active association already exists for user {UserId} and product mapping {MappingId}",
                    rewardEvent.UserId, productMapping.Id);
                return activeAssociation;
            }

            // Create new association
            var association = new ProductMappingUserAssociation
            {
                UserId = rewardEvent.UserId,
                ProductMappingId = productMapping.Id,
                ProviderId = rewardEvent.ProviderId,
                ProviderProductId = tierId,
                AssignedAt = DateTime.UtcNow,
                Status = AssociationStatus.Active
            };

            // Add source information to metadata
            association.Metadata["source"] = $"patreon_webhook_{rewardEvent.EventType.ToString().ToLower()}";

            await _associationRepository.Create(association);

            _logger.LogInformation("Created association {AssociationId} for user {UserId} with product mapping {MappingId} (tier: {TierId})",
                association.Id, rewardEvent.UserId, productMapping.Id, tierId);

            return association;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating association for user {UserId} and tier {TierId}", rewardEvent.UserId, tierId);
            throw;
        }
    }
}
