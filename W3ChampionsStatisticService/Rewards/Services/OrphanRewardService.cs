using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using W3C.Domain.Rewards.Abstractions;
using W3C.Domain.Rewards.Entities;
using W3C.Domain.Rewards.Repositories;
using W3C.Domain.Rewards.ValueObjects;
using W3ChampionsStatisticService.Rewards.Providers.Patreon;

namespace W3ChampionsStatisticService.Rewards.Services;

public class OrphanRewardService(
    IRewardAssignmentRepository assignmentRepository,
    IProductMappingUserAssociationRepository associationRepository,
    IPatreonAccountLinkRepository linkRepository,
    PatreonApiClient patreonApiClient,
    IRewardService rewardService,
    ILogger<OrphanRewardService> logger) : IOrphanRewardService
{
    private const string ProviderId = "patreon";

    private readonly IRewardAssignmentRepository _assignmentRepository = assignmentRepository;
    private readonly IProductMappingUserAssociationRepository _associationRepository = associationRepository;
    private readonly IPatreonAccountLinkRepository _linkRepository = linkRepository;
    private readonly PatreonApiClient _patreonApiClient = patreonApiClient;
    private readonly IRewardService _rewardService = rewardService;
    private readonly ILogger<OrphanRewardService> _logger = logger;

    public async Task<OrphanRewardReport> DetectOrphans()
    {
        var report = new OrphanRewardReport { DetectedAtUtc = DateTime.UtcNow };

        var activePatreonRAs = await _assignmentRepository.GetActiveAssignmentsByProvider(ProviderId);
        if (!activePatreonRAs.Any()) return report;

        var activeAssociations = await _associationRepository.GetAll(AssociationStatus.Active);
        var userIdsWithActivePmua = activeAssociations
            .Where(a => a.ProviderId == ProviderId && a.IsActive())
            .Select(a => a.UserId)
            .ToHashSet();

        var allLinks = await _linkRepository.GetAll();
        var patreonUserIdToBattleTag = allLinks.ToDictionary(l => l.PatreonUserId, l => l.BattleTag);

        var allMembers = await _patreonApiClient.GetAllCampaignMembers();
        var activePatronBattleTags = allMembers
            .Where(m => m.IsActivePatron && !string.IsNullOrEmpty(m.PatreonUserId))
            .Select(m => patreonUserIdToBattleTag.GetValueOrDefault(m.PatreonUserId))
            .Where(bt => !string.IsNullOrEmpty(bt))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var grouped = activePatreonRAs.GroupBy(ra => ra.UserId);
        foreach (var group in grouped)
        {
            var userId = group.Key;
            if (string.IsNullOrEmpty(userId)) continue;

            if (userIdsWithActivePmua.Contains(userId)) continue;       // healthy
            if (activePatronBattleTags.Contains(userId)) continue;      // missing-but-reactivating; will heal via drift

            report.Entries.Add(new OrphanRewardEntry
            {
                UserId = userId,
                Reason = "Active patreon RewardAssignment with no backing active PMUA and not an active patron",
                Assignments = group.Select(ra => new OrphanAssignmentDetail
                {
                    AssignmentId = ra.Id,
                    RewardId = ra.RewardId,
                    ProviderReference = ra.ProviderReference,
                    AssignedAt = ra.AssignedAt
                }).ToList()
            });
        }

        _logger.LogInformation("Orphan detection: {Count} users with orphan rewards", report.Entries.Count);
        return report;
    }

    public async Task<OrphanRewardRevocationResult> RevokeOrphans(IReadOnlySet<string> approvedUserIds, string actorBattleTag)
    {
        var result = new OrphanRewardRevocationResult { ExecutedAtUtc = DateTime.UtcNow };

        // Re-detect to make the decision against fresh data — never revoke based on a stale report.
        var freshReport = await DetectOrphans();
        var freshUserIds = freshReport.Entries.Select(e => e.UserId).ToHashSet();

        foreach (var requestedUserId in approvedUserIds)
        {
            if (!freshUserIds.Contains(requestedUserId))
            {
                result.SkippedUserIdsNotInLatestDetection.Add(requestedUserId);
                _logger.LogWarning("Skipping orphan revocation for {UserId} — no longer in fresh orphan detection (actor: {Actor})",
                    requestedUserId, actorBattleTag);
                continue;
            }

            var entry = freshReport.Entries.First(e => e.UserId == requestedUserId);
            foreach (var assignment in entry.Assignments)
            {
                try
                {
                    await _rewardService.RevokeReward(assignment.AssignmentId,
                        $"Admin orphan cleanup by {actorBattleTag}: no backing active PMUA, not active patron");
                    result.AssignmentsRevoked++;
                    _logger.LogInformation("Admin orphan cleanup revoked {AssignmentId} for {UserId} (actor: {Actor})",
                        assignment.AssignmentId, requestedUserId, actorBattleTag);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Admin orphan cleanup failed for {AssignmentId} ({UserId})",
                        assignment.AssignmentId, requestedUserId);
                    result.Errors.Add($"{assignment.AssignmentId}: {ex.Message}");
                }
            }
            result.UsersTouched++;
        }

        return result;
    }
}
