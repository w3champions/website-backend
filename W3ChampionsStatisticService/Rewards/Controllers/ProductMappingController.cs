using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using W3C.Domain.Rewards.Abstractions;
using W3C.Domain.Rewards.Entities;
using W3C.Domain.Rewards.Exceptions;
using W3C.Domain.Common.Repositories;
using W3C.Domain.Common.Services;
using W3C.Domain.Rewards.Repositories;
using W3ChampionsStatisticService.Rewards.DTOs;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.Rewards.Controllers;

/// <summary>
/// Controller for managing product mappings between providers and rewards
/// </summary>
[ApiController]
[Route("api/rewards/product-mappings")]
public class ProductMappingController(
    IProductMappingRepository productMappingRepo,
    IProductMappingUserAssociationRepository associationRepo,
    IProductMappingReconciliationService reconciliationService,
    IAuditLogService auditLogService,
    ILogger<ProductMappingController> logger) : ControllerBase
{
    private readonly IProductMappingRepository _productMappingRepo = productMappingRepo;
    private readonly IProductMappingUserAssociationRepository _associationRepo = associationRepo;
    private readonly IProductMappingReconciliationService _reconciliationService = reconciliationService;
    private readonly IAuditLogService _auditLogService = auditLogService;
    private readonly ILogger<ProductMappingController> _logger = logger;

    [HttpGet]
    [CheckIfBattleTagIsAdmin]
    public async Task<IActionResult> GetProductMappings()
    {
        var mappings = await _productMappingRepo.GetAll();
        return Ok(mappings);
    }

    [HttpGet("{id}/users")]
    [CheckIfBattleTagIsAdmin]
    public async Task<IActionResult> GetProductMappingUsers(string id)
    {
        var associations = await _associationRepo.GetUsersByProductMappingId(id);

        var users = associations.Select(a => new
        {
            userId = a.UserId,
            productMappingId = a.ProductMappingId,
            providerId = a.ProviderId,
            providerProductId = a.ProviderProductId,
            assignedAt = a.AssignedAt,
            expiresAt = a.ExpiresAt,
            status = a.Status.ToString(),
            isActive = a.IsActive(),
            metadata = a.Metadata
        }).ToList();

        return Ok(new
        {
            productMappingId = id,
            userCount = users.Count,
            users = users
        });
    }

    [HttpPost]
    [CheckIfBattleTagIsAdmin]
    public async Task<IActionResult> CreateProductMapping([FromBody] CreateProductMappingRequest request, string battleTag)
    {
        try
        {
            // Convert DTO to entity
            var mapping = new ProductMapping
            {
                Id = Guid.NewGuid().ToString(),
                ProductName = request.ProductName,
                ProductProviders = request.ProductProviders.Select(pp => new ProductProviderPair(pp.ProviderId, pp.ProductId)).ToList(),
                RewardIds = request.RewardIds,
                Type = request.Type,
                IsActive = true,
                AdditionalParameters = request.AdditionalParameters,
                CreatedAt = DateTime.UtcNow
            };

            await _productMappingRepo.Create(mapping);
            _logger.LogInformation("Created product mapping {MappingId}: {ProductName} -> {RewardIds}",
                mapping.Id, mapping.ProductName, string.Join(", ", mapping.RewardIds));

            // Log audit event
            await _auditLogService.LogAdminAction(battleTag, "CREATE", "ProductMapping", mapping.Id,
                oldValue: null, newValue: mapping);

            return Ok(mapping);
        }
        catch (ProductMappingException ex)
        {
            _logger.LogWarning(ex, "Product mapping creation failed: {ProductName}", request.ProductName);
            return BadRequest(new { error = ex.Message, errorCode = ex.ErrorCode });
        }
        catch (RewardsValidationException ex)
        {
            _logger.LogWarning(ex, "Validation failed for product mapping: {ProductName}", request.ProductName);
            return BadRequest(new { error = ex.Message, errorCode = ex.ErrorCode });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating product mapping: {ProductName}", request.ProductName);
            throw new ProductMappingException($"Failed to create product mapping: {request.ProductName}", ex);
        }
    }

    [HttpPut("{id}")]
    [CheckIfBattleTagIsAdmin]
    public async Task<IActionResult> UpdateProductMapping(string id, [FromBody] UpdateProductMappingRequest request, string battleTag)
    {
        try
        {
            var existingMapping = await _productMappingRepo.GetById(id);
            if (existingMapping == null)
            {
                throw new RewardsNotFoundException("ProductMapping", id);
            }

            // Store original for audit logging
            var originalMapping = new ProductMapping
            {
                Id = existingMapping.Id,
                ProductName = existingMapping.ProductName,
                ProductProviders = existingMapping.ProductProviders,
                RewardIds = existingMapping.RewardIds,
                Type = existingMapping.Type,
                IsActive = existingMapping.IsActive,
                AdditionalParameters = existingMapping.AdditionalParameters,
                CreatedAt = existingMapping.CreatedAt,
                UpdatedAt = existingMapping.UpdatedAt
            };

            // Check if rewards are being changed to determine if reconciliation is needed
            bool rewardsChanged = false;
            if (request.RewardIds != null)
            {
                rewardsChanged = !existingMapping.RewardIds.SequenceEqual(request.RewardIds);
            }

            // Update all properties
            if (request.ProductName != null)
                existingMapping.ProductName = request.ProductName;

            if (request.IsActive.HasValue)
                existingMapping.IsActive = request.IsActive.Value;

            if (request.AdditionalParameters != null)
                existingMapping.AdditionalParameters = request.AdditionalParameters;

            if (request.RewardIds != null)
                existingMapping.RewardIds = request.RewardIds;

            if (request.ProductProviders != null)
                existingMapping.ProductProviders = request.ProductProviders.Select(pp => new ProductProviderPair(pp.ProviderId, pp.ProductId)).ToList();

            if (request.Type.HasValue)
                existingMapping.Type = request.Type.Value;

            existingMapping.UpdatedAt = DateTime.UtcNow;

            await _productMappingRepo.Update(existingMapping);
            _logger.LogInformation("Updated product mapping {MappingId}", id);

            // Log audit event
            await _auditLogService.LogAdminAction(battleTag, "UPDATE", "ProductMapping", id,
                oldValue: originalMapping, newValue: existingMapping);

            // Trigger automatic reconciliation if rewards changed
            if (rewardsChanged)
            {
                _logger.LogInformation("Rewards changed for product mapping {MappingId}, triggering automatic reconciliation", id);

                // Run reconciliation asynchronously to avoid blocking the response
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var eventIdPrefix = $"auto_update_{id}_{DateTime.UtcNow:yyyyMMddHHmmss}";
                        var reconciliationResult = await _reconciliationService.ReconcileProductMapping(
                            id,
                            originalMapping,
                            existingMapping,
                            dryRun: false);

                        _logger.LogInformation("Automatic reconciliation completed for mapping {MappingId}: Added={Added}, Revoked={Revoked}, Success={Success}",
                            id, reconciliationResult.RewardsAdded, reconciliationResult.RewardsRevoked, reconciliationResult.Success);

                        // Log audit event for the automatic reconciliation
                        await _auditLogService.LogAdminAction(battleTag, "AUTO_RECONCILE", "ProductMapping", id,
                            metadata: new Dictionary<string, object>
                            {
                                ["triggered_by"] = "product_mapping_update",
                                ["rewards_added"] = reconciliationResult.RewardsAdded,
                                ["rewards_revoked"] = reconciliationResult.RewardsRevoked,
                                ["users_affected"] = reconciliationResult.TotalUsersAffected,
                                ["errors"] = reconciliationResult.Errors.Count
                            });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Automatic reconciliation failed for product mapping {MappingId}", id);
                    }
                });
            }

            return Ok(existingMapping);
        }
        catch (ProductMappingException ex)
        {
            _logger.LogWarning(ex, "Product mapping update failed: {MappingId}", id);
            return BadRequest(new { error = ex.Message, errorCode = ex.ErrorCode });
        }
        catch (RewardsValidationException ex)
        {
            _logger.LogWarning(ex, "Validation failed for product mapping update: {MappingId}", id);
            return BadRequest(new { error = ex.Message, errorCode = ex.ErrorCode });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error updating product mapping {MappingId}", id);
            throw new ProductMappingException($"Failed to update product mapping: {id}", ex);
        }
    }

    [HttpDelete("{id}")]
    [CheckIfBattleTagIsAdmin]
    public async Task<IActionResult> DeleteProductMapping(string id, string battleTag)
    {
        try
        {
            var mapping = await _productMappingRepo.GetById(id);
            if (mapping == null)
            {
                throw new RewardsNotFoundException("ProductMapping", id);
            }

            // Check for active associations
            var activeAssociations = await _associationRepo.GetUsersByProductMappingId(id);
            var hasActiveUsers = activeAssociations.Any(a => a.IsActive());

            if (hasActiveUsers)
            {
                var activeUserCount = activeAssociations.Count(a => a.IsActive());
                return BadRequest(new
                {
                    error = $"Cannot delete product mapping: It has {activeUserCount} active user association(s). " +
                           "Wait for associations to expire or manually revoke them before deleting."
                });
            }

            await _productMappingRepo.Delete(id);
            _logger.LogInformation("Deleted product mapping {MappingId}: {ProductName}", id, mapping.ProductName);

            // Log audit event
            await _auditLogService.LogAdminAction(battleTag, "DELETE", "ProductMapping", id,
                oldValue: mapping, newValue: null);

            return NoContent();
        }
        catch (ProductMappingException ex)
        {
            _logger.LogWarning(ex, "Product mapping deletion failed: {MappingId}", id);
            return BadRequest(new { error = ex.Message, errorCode = ex.ErrorCode });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error deleting product mapping {MappingId}", id);
            throw new ProductMappingException($"Failed to delete product mapping: {id}", ex);
        }
    }
}
