using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using W3C.Domain.Rewards.Abstractions;
using W3C.Domain.Common.Services;
using W3C.Domain.Rewards.Repositories;
using W3C.Domain.Rewards.ValueObjects;
using W3ChampionsStatisticService.Rewards.DTOs;
using W3ChampionsStatisticService.Rewards.Services;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.Rewards.Controllers;

/// <summary>
/// Controller for admin-only reward operations and monitoring
/// </summary>
[ApiController]
[Route("api/rewards/admin")]
public class AdminRewardController(
    IRewardAssignmentRepository assignmentRepo,
    IPatreonAccountLinkRepository patreonLinkRepo,
    IProductMappingRepository productMappingRepo,
    IProductMappingReconciliationService reconciliationService,
    PatreonDriftDetectionService patreonDriftService,
    IAuditLogService auditLogService,
    ILogger<AdminRewardController> logger) : ControllerBase
{
    private readonly IRewardAssignmentRepository _assignmentRepo = assignmentRepo;
    private readonly IPatreonAccountLinkRepository _patreonLinkRepo = patreonLinkRepo;
    private readonly IProductMappingRepository _productMappingRepo = productMappingRepo;
    private readonly IProductMappingReconciliationService _reconciliationService = reconciliationService;
    private readonly PatreonDriftDetectionService _patreonDriftService = patreonDriftService;
    private readonly IAuditLogService _auditLogService = auditLogService;
    private readonly ILogger<AdminRewardController> _logger = logger;

    [HttpGet("summary")]
    [CheckIfBattleTagIsAdmin]
    public async Task<IActionResult> GetRewardManagementSummary()
    {
        try
        {
            var allAssignments = await _assignmentRepo.GetAll();

            var summary = new
            {
                totalRewards = 0, // Would need IRewardRepository to get actual count
                activeRewards = 0,
                inactiveRewards = 0,
                totalAssignments = allAssignments.Count,
                activeAssignments = allAssignments.Count(a => a.Status == RewardStatus.Active),
                expiredAssignments = allAssignments.Count(a => a.Status == RewardStatus.Expired),
                revokedAssignments = allAssignments.Count(a => a.Status == RewardStatus.Revoked),
                failedAssignments = allAssignments.Count(a => a.Status == RewardStatus.Failed),
                totalUsers = allAssignments.Select(a => a.UserId).Distinct().Count(),
                usersWithActiveRewards = allAssignments.Where(a => a.Status == RewardStatus.Active).Select(a => a.UserId).Distinct().Count(),
                recentAssignments = allAssignments.OrderByDescending(a => a.AssignedAt).Take(10).ToList(),
                problematicAssignments = allAssignments.Where(a => a.Status == RewardStatus.Failed || !string.IsNullOrEmpty(a.RevokedReason)).Take(10).ToList()
            };

            return Ok(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting reward management summary");
            return StatusCode(500, new { error = "Failed to get summary" });
        }
    }

    [HttpGet("assignments/{userId}")]
    [CheckIfBattleTagIsAdmin]
    public async Task<IActionResult> GetUserRewards(string userId)
    {
        var assignments = await _assignmentRepo.GetByUserId(userId);
        return Ok(assignments);
    }

    [HttpGet("assignments/all")]
    [CheckIfBattleTagIsAdmin]
    public async Task<IActionResult> GetAllAssignments([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        try
        {
            // Validate pagination parameters
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 100;

            var (assignments, totalCount) = await _assignmentRepo.GetAllPaginated(page, pageSize);

            var response = new
            {
                assignments = assignments.Select(a => new UserRewardDto
                {
                    AssignmentId = a.Id,
                    UserId = a.UserId,
                    RewardId = a.RewardId,
                    ProviderId = a.ProviderId,
                    ProviderReference = a.ProviderReference,
                    Status = a.Status,
                    AssignedAt = a.AssignedAt,
                    ExpiresAt = a.ExpiresAt,
                    RevokedAt = a.RevokedAt,
                    RevocationReason = a.RevokedReason,
                    EventId = a.EventId,
                    Metadata = a.Metadata
                }),
                pagination = new
                {
                    currentPage = page,
                    pageSize = pageSize,
                    totalCount = totalCount,
                    totalPages = (int)Math.Ceiling((double)totalCount / pageSize),
                    hasNextPage = page * pageSize < totalCount,
                    hasPreviousPage = page > 1
                }
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving paginated assignments");
            return StatusCode(500, new { error = "Failed to retrieve assignments" });
        }
    }

    [HttpGet("rewards/{rewardId}/assignments")]
    [CheckIfBattleTagIsAdmin]
    public async Task<IActionResult> GetAssignmentsByReward(string rewardId)
    {
        try
        {
            var assignments = await _assignmentRepo.GetByRewardId(rewardId);

            var groupedAssignments = assignments
                .GroupBy(a => a.Status)
                .ToDictionary(g => g.Key.ToString(), g => g.ToList());

            var response = new
            {
                rewardId = rewardId,
                totalAssignments = assignments.Count,
                assignmentsByStatus = groupedAssignments,
                assignments = assignments.Select(a => new UserRewardDto
                {
                    AssignmentId = a.Id,
                    UserId = a.UserId,
                    RewardId = a.RewardId,
                    ProviderId = a.ProviderId,
                    ProviderReference = a.ProviderReference,
                    Status = a.Status,
                    AssignedAt = a.AssignedAt,
                    ExpiresAt = a.ExpiresAt,
                    RevokedAt = a.RevokedAt,
                    RevocationReason = a.RevokedReason,
                    EventId = a.EventId,
                    Metadata = a.Metadata
                })
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving assignments for reward {RewardId}", rewardId);
            return StatusCode(500, new { error = "Failed to retrieve reward assignments" });
        }
    }

    [HttpGet("patreon/links")]
    [CheckIfBattleTagIsAdmin]
    public async Task<IActionResult> GetAllPatreonLinks()
    {
        try
        {
            var links = await _patreonLinkRepo.GetAll();

            var response = links.Select(link => new PatreonAccountLinkDto
            {
                Id = link.Id.ToString(),
                BattleTag = link.BattleTag,
                PatreonUserId = link.PatreonUserId,
                LinkedAt = link.LinkedAt,
                LastSyncAt = link.LastSyncAt,
                Metadata = link.Metadata
            });

            return Ok(new
            {
                totalLinks = links.Count,
                links = response
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving Patreon account links");
            return StatusCode(500, new { error = "Failed to retrieve Patreon links" });
        }
    }

    [HttpDelete("patreon/links/{battleTag}")]
    [CheckIfBattleTagIsAdmin]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> DeletePatreonLink(string battleTag, string actingPlayer)
    {
        try
        {
            var accountLink = await _patreonLinkRepo.GetByBattleTag(battleTag);
            if (accountLink == null)
            {
                return NotFound(new { error = $"No Patreon link found for BattleTag: {battleTag}" });
            }

            var patreonUserId = accountLink.PatreonUserId;

            // Delete the account link
            await _patreonLinkRepo.Delete(accountLink.Id);

            _logger.LogInformation("Admin {AdminBattleTag} deleted Patreon link for user {BattleTag} (was PatreonUserId: {PatreonUserId})",
                actingPlayer, battleTag, patreonUserId);

            // Log audit event
            await _auditLogService.LogAdminAction(actingPlayer, "DELETE", "PatreonAccountLink", accountLink.Id.ToString(),
                oldValue: accountLink, newValue: null);

            return Ok(new
            {
                success = true,
                battleTag = battleTag,
                patreonUserId = patreonUserId,
                message = "Patreon account link deleted successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting Patreon link for BattleTag {BattleTag}", battleTag);
            return StatusCode(500, new { error = "Failed to delete Patreon link", details = ex.Message });
        }
    }

    [HttpGet("patreon/members/{battleTag}")]
    [CheckIfBattleTagIsAdmin]
    public async Task<IActionResult> GetPatreonMemberDetails(string battleTag)
    {
        try
        {
            // Check if Patreon link exists
            var accountLink = await _patreonLinkRepo.GetByBattleTag(battleTag);
            if (accountLink == null)
            {
                return NotFound(new { error = $"No Patreon link found for BattleTag: {battleTag}" });
            }

            // Fetch member details from Patreon API
            var memberDetails = await _patreonDriftService.GetPatreonMemberDetails(battleTag, accountLink.PatreonUserId);

            if (!memberDetails.Found)
            {
                return Ok(new
                {
                    found = false,
                    battleTag = battleTag,
                    patreonUserId = accountLink.PatreonUserId,
                    error = memberDetails.ErrorMessage,
                    message = "User has a Patreon link but was not found in current campaign members"
                });
            }

            return Ok(new
            {
                found = true,
                battleTag = memberDetails.BattleTag,
                patreonUserId = memberDetails.PatreonUserId,
                patreonMemberId = memberDetails.PatreonMemberId,
                email = memberDetails.Email,
                patronStatus = memberDetails.PatronStatus,
                isActivePatron = memberDetails.IsActivePatron,
                entitledTierIds = memberDetails.EntitledTierIds,
                lastChargeDate = memberDetails.LastChargeDate?.ToString("O"),
                lastChargeStatus = memberDetails.LastChargeStatus,
                pledgeRelationshipStart = memberDetails.PledgeRelationshipStart?.ToString("O"),
                activeAssociationCount = memberDetails.ActiveAssociationCount,
                activeAssociationTiers = memberDetails.ActiveAssociationTiers
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Patreon member details for {BattleTag}", battleTag);
            return StatusCode(500, new { error = "Failed to fetch member details", details = ex.Message });
        }
    }

    [HttpGet("product-mappings/{id}/reconciliation-summary")]
    [CheckIfBattleTagIsAdmin]
    public async Task<IActionResult> GetProductMappingReconciliationSummary(string id)
    {
        try
        {
            var result = await _reconciliationService.PreviewReconciliation(id);

            return Ok(new
            {
                productMappingId = id,
                productMappingName = result.ProductMappingName ?? "Unknown",
                success = result.Success,
                usersProcessed = result.UserReconciliations?.Count ?? 0,
                rewardsAdded = result.RewardsAdded,
                rewardsRevoked = result.RewardsRevoked,
                errors = result.Errors ?? new List<string>(),
                userActions = result.UserReconciliations ?? new List<UserReconciliationEntry>(),
                wasDryRun = true,
                processedAt = DateTime.UtcNow.ToString("O")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting product mapping reconciliation summary for {MappingId}", id);
            return StatusCode(500, new { error = "Failed to get reconciliation summary" });
        }
    }

    [HttpGet("product-mappings/{id}/reconcile/preview")]
    [CheckIfBattleTagIsAdmin]
    public async Task<IActionResult> PreviewProductMappingReconciliation(string id)
    {
        try
        {
            _logger.LogInformation("Previewing reconciliation for product mapping {MappingId}", id);

            var result = await _reconciliationService.PreviewReconciliation(id);

            return Ok(new
            {
                productMappingId = id,
                dryRun = true,
                rewardsAdded = result.RewardsAdded,
                rewardsRevoked = result.RewardsRevoked,
                errors = result.Errors,
                userReconciliations = result.UserReconciliations,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error previewing reconciliation for product mapping {MappingId}", id);
            return StatusCode(500, new { error = "Failed to preview reconciliation", details = ex.Message });
        }
    }

    [HttpPost("product-mappings/{id}/reconcile")]
    [CheckIfBattleTagIsAdmin]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> ReconcileProductMapping(string id, string actingPlayer, [FromQuery] bool dryRun = false)
    {
        try
        {
            _logger.LogInformation("Starting reconciliation for product mapping {MappingId}. DryRun: {DryRun}", id, dryRun);

            var currentMapping = await _productMappingRepo.GetById(id);
            if (currentMapping == null)
            {
                return NotFound(new { error = "Product mapping not found" });
            }

            var result = await _reconciliationService.ReconcileProductMapping(id, null, currentMapping, dryRun);

            if (!dryRun)
            {
                // Log audit event for actual reconciliation
                await _auditLogService.LogAdminAction(actingPlayer, "RECONCILE", "ProductMapping", id,
                    metadata: new Dictionary<string, object>
                    {
                        ["rewards_added"] = result.RewardsAdded,
                        ["rewards_revoked"] = result.RewardsRevoked,
                        ["errors"] = result.Errors.Count
                    });
            }

            return Ok(new
            {
                productMappingId = id,
                dryRun = dryRun,
                rewardsAdded = result.RewardsAdded,
                rewardsRevoked = result.RewardsRevoked,
                errors = result.Errors,
                userReconciliations = result.UserReconciliations,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during reconciliation for product mapping {MappingId}", id);
            return StatusCode(500, new { error = "Reconciliation failed", details = ex.Message });
        }
    }

    [HttpPost("product-mappings/reconcile-all")]
    [CheckIfBattleTagIsAdmin]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> ReconcileAllProductMappings(string actingPlayer, [FromQuery] bool dryRun = false)
    {
        try
        {
            _logger.LogInformation("Starting bulk reconciliation for all product mappings. DryRun: {DryRun}", dryRun);

            var result = await _reconciliationService.ReconcileAllMappings(dryRun);

            if (!dryRun)
            {
                // Log audit event for bulk reconciliation
                await _auditLogService.LogAdminAction(actingPlayer, "BULK_RECONCILE", "ProductMapping", "all",
                    metadata: new Dictionary<string, object>
                    {
                        ["total_rewards_added"] = result.RewardsAdded,
                        ["total_rewards_revoked"] = result.RewardsRevoked,
                        ["users_affected"] = result.TotalUsersAffected,
                        ["total_errors"] = result.Errors.Count
                    });
            }

            return Ok(new
            {
                dryRun = dryRun,
                usersAffected = result.TotalUsersAffected,
                totalRewardsAdded = result.RewardsAdded,
                totalRewardsRevoked = result.RewardsRevoked,
                totalErrors = result.Errors.Count,
                userReconciliations = result.UserReconciliations,
                success = result.Success,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during bulk reconciliation");
            return StatusCode(500, new { error = "Bulk reconciliation failed", details = ex.Message });
        }
    }
}
