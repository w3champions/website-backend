using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using W3C.Domain.Rewards.Abstractions;
using W3C.Domain.Rewards.Entities;
using W3C.Domain.Rewards.Repositories;
using W3C.Domain.Rewards.ValueObjects;
using W3ChampionsStatisticService.Rewards.Providers.Patreon;
using W3ChampionsStatisticService.Rewards.Services;

namespace WC3ChampionsStatisticService.Tests.Rewards;

/// <summary>
/// Regression test for the orphan RewardAssignment bug. Wires up a REAL
/// ProductMappingReconciliationService (not mocked) and asserts that drift sync
/// revokes RewardAssignment rows end-to-end via the IRewardService.RevokeReward
/// path. This is the test that would have caught the original production bug —
/// the prior mock-based fixture in PatreonDriftSyncTests stubbed the reconciler
/// to always return RewardsRevoked=0, which is exactly the bug.
/// </summary>
[TestFixture]
public class PatreonDriftRevocationIntegrationTests
{
    [Test]
    public async Task LapsedPatron_DriftSync_RevokesRewardAssignmentEndToEnd()
    {
        // Arrange — Bubu#23550 has 1 active PMUA + 2 active patreon RAs, but no longer
        // an active patron in Patreon. Drift sync must revoke the RAs end-to-end.
        var userId = "Bubu#23550";

        var mockPatreonApiClient = new Mock<PatreonApiClient>(Mock.Of<HttpClient>());
        mockPatreonApiClient.Setup(x => x.GetAllCampaignMembers()).ReturnsAsync(new List<PatreonMember>());

        var mockAssociationRepo = new Mock<IProductMappingUserAssociationRepository>();
        var existingPmua = new ProductMappingUserAssociation
        {
            Id = "pmua-1",
            UserId = userId,
            ProductMappingId = "mapping-grandmaster",
            ProviderId = "patreon",
            ProviderProductId = "6482092",
            Status = AssociationStatus.Active,
            AssignedAt = DateTime.UtcNow.AddMonths(-3)
        };
        mockAssociationRepo.Setup(x => x.GetAll(AssociationStatus.Active))
            .ReturnsAsync(new List<ProductMappingUserAssociation> { existingPmua });
        mockAssociationRepo.Setup(x => x.GetProductMappingsByUserId(userId))
            .ReturnsAsync(new List<ProductMappingUserAssociation> { existingPmua });
        mockAssociationRepo.Setup(x => x.Update(It.IsAny<ProductMappingUserAssociation>()))
            .ReturnsAsync((ProductMappingUserAssociation a) => a);

        var grandmasterMapping = new ProductMapping
        {
            Id = "mapping-grandmaster",
            ProductName = "Grand Master",
            RewardIds = new List<string> { "reward-grandmaster-icon", "reward-grandmaster-portrait" },
            ProductProviders = new List<ProductProviderPair>
            {
                new ProductProviderPair { ProviderId = "patreon", ProductId = "6482092" }
            }
        };

        var mockProductMappingRepo = new Mock<IProductMappingRepository>();
        mockProductMappingRepo.Setup(x => x.GetById("mapping-grandmaster"))
            .ReturnsAsync(grandmasterMapping);
        mockProductMappingRepo.Setup(x => x.GetByProviderId("patreon"))
            .ReturnsAsync(new List<ProductMapping> { grandmasterMapping });

        var mockPatreonLinkRepo = new Mock<IPatreonAccountLinkRepository>();
        mockPatreonLinkRepo.Setup(x => x.GetAll()).ReturnsAsync(new List<PatreonAccountLink>());
        mockPatreonLinkRepo.Setup(x => x.RefreshLastSyncAt(It.IsAny<string>())).Returns(Task.CompletedTask);

        // Track RA state — model what the real RevokeReward would do
        var revokedAssignmentIds = new HashSet<string>();
        var allActiveRAs = new List<RewardAssignment>
        {
            new RewardAssignment
            {
                Id = "ra-1", UserId = userId, RewardId = "reward-grandmaster-icon",
                ProviderId = "patreon", Status = RewardStatus.Active,
                ProviderReference = "reconciliation:mapping-grandmaster"
            },
            new RewardAssignment
            {
                Id = "ra-2", UserId = userId, RewardId = "reward-grandmaster-portrait",
                ProviderId = "patreon", Status = RewardStatus.Active,
                ProviderReference = "reconciliation:mapping-grandmaster"
            }
        };

        var mockRewardAssignmentRepo = new Mock<IRewardAssignmentRepository>();
        // Filter out revoked assignments on each query (simulates DB state)
        mockRewardAssignmentRepo.Setup(x => x.GetByUserIdAndStatus(userId, RewardStatus.Active))
            .ReturnsAsync(() => allActiveRAs.Where(ra => !revokedAssignmentIds.Contains(ra.Id)).ToList());
        mockRewardAssignmentRepo.Setup(x => x.GetActiveAssignmentsByProvider("patreon"))
            .ReturnsAsync(() => allActiveRAs.Where(ra => !revokedAssignmentIds.Contains(ra.Id)).ToList());

        var mockRewardService = new Mock<IRewardService>();
        mockRewardService.Setup(x => x.RevokeReward(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((id, reason) => revokedAssignmentIds.Add(id))
            .Returns(Task.CompletedTask);
        // GetUserRewards must reflect the same state — drives the bidirectional reconciler
        mockRewardService.Setup(x => x.GetUserRewards(userId))
            .ReturnsAsync(() => allActiveRAs.Where(ra => !revokedAssignmentIds.Contains(ra.Id)).ToList());

        var mockProductMappingService = new Mock<IProductMappingService>();
        // Post-PMUA-revoke: empty (PMUA was revoked by the point-fix, drift run is now in reconciler phase)
        mockProductMappingService.Setup(x => x.GetUserAssociationsByUserId(userId))
            .ReturnsAsync(new List<ProductMappingUserAssociation>());

        // REAL reconciliation service — the integration boundary we care about
        var realReconciler = new ProductMappingReconciliationService(
            mockProductMappingService.Object,
            mockRewardService.Object,
            Mock.Of<ILogger<ProductMappingReconciliationService>>());

        var service = new PatreonDriftDetectionService(
            mockPatreonApiClient.Object,
            mockAssociationRepo.Object,
            mockProductMappingRepo.Object,
            mockPatreonLinkRepo.Object,
            realReconciler,
            mockRewardAssignmentRepo.Object,
            mockRewardService.Object);

        // Act
        var driftResult = await service.DetectDrift();
        await service.SyncDrift(driftResult, dryRun: false);

        // Assert — both RAs were revoked end-to-end via the integration boundary
        Assert.That(revokedAssignmentIds, Is.EquivalentTo(new[] { "ra-1", "ra-2" }),
            "Both orphan RAs must be revoked end-to-end. If this fails, the drift sync " +
            "is silently leaving RewardAssignments active after revoking PMUAs — the exact " +
            "production bug this test was created to prevent.");
    }
}
